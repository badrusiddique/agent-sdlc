import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnDestroy, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription, interval, switchMap, catchError, of } from 'rxjs';
import { PipelineApi } from '../core/pipeline.api';
import {
  ArtifactContent,
  ArtifactListItem,
  ChatMessage,
  LogEntry,
  PipelineEvent,
  PipelineStatus,
} from '../core/pipeline.models';

type TabId = 'chat' | 'docs' | 'logs';

interface ExamplePrompt {
  label: string;
  text: string;
  stackProfile: string;
  projectName: string;
  projectMode: 'New' | 'Existing' | 'Rewrite';
  baselineArtifacts?: Record<string, string>;
  /** POC Manual QA: poison first TA verify so RouteBack → IA once. */
  simulateTaRouteback?: boolean;
}

@Component({
  selector: 'app-studio-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './studio-page.component.html',
  styleUrl: './studio-page.component.scss',
})
export class StudioPageComponent implements OnDestroy {
  private readonly api = inject(PipelineApi);

  @ViewChild('chatScroll') chatScroll?: ElementRef<HTMLElement>;

  readonly examples: ExamplePrompt[] = [
    {
      label: 'Hello World',
      text: 'Build a Hello World app: Angular FE + .NET 10 BE + an LLM giving a thought of the day',
      stackProfile: 'fe-be-llm',
      projectName: 'Hello World',
      projectMode: 'New',
    },
    {
      label: 'Hello (no LLM)',
      text: 'Build a simple Hello World app without an LLM',
      stackProfile: 'fe-be',
      projectName: 'HelloNoLlm',
      projectMode: 'New',
    },
    {
      label: 'Calculator add',
      text: 'Create a calculator app that adds two numbers',
      stackProfile: 'fe-be',
      projectName: 'Calculator',
      projectMode: 'New',
    },
    {
      label: 'Calculator subtract',
      text: 'Update the calculator app to subtract two numbers',
      stackProfile: 'fe-be',
      projectName: 'Calculator',
      projectMode: 'Existing',
    },
    {
      label: 'Todo list',
      text: 'Create a todo list app',
      stackProfile: 'fe-be',
      projectName: 'Todo',
      projectMode: 'New',
    },
    {
      label: 'Rewrite legacy → net10',
      text: 'Rewrite the existing net8 calculator repo as a greenfield .NET 10 app keeping /api/add',
      stackProfile: 'fe-be',
      projectName: 'CalculatorRewrite',
      projectMode: 'Rewrite',
      baselineArtifacts: { baselineSourcePath: 'fixtures/legacy-net8-calculator' },
    },
    {
      label: 'RouteBack probe',
      text: 'Create a calculator app that adds two numbers (RouteBack QA probe)',
      stackProfile: 'fe-be',
      projectName: 'RouteBackProbe',
      projectMode: 'New',
      simulateTaRouteback: true,
    },
  ];

  draft = '';
  activeTab: TabId = 'chat';
  busy = signal(false);
  approving = signal(false);
  steering = signal(false);
  status = signal<PipelineStatus | null>(null);
  messages = signal<ChatMessage[]>([]);
  artifacts = signal<ArtifactListItem[]>([]);
  logs = signal<LogEntry[]>([]);
  selectedArtifact = signal<ArtifactContent | null>(null);
  selectedArtifactKey = signal<string | null>(null);
  error = signal<string | null>(null);
  lastProjectId: string | null = null;

  private pollSub?: Subscription;
  private startSub?: Subscription;
  private approveSub?: Subscription;
  private steerSub?: Subscription;
  private seenEventIds = new Set<string>();
  private workItemId: string | null = null;

  private static readonly SteerCommands = new Set(['PAUSE', 'STOP', 'ABORT', 'CONTINUE']);

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.startSub?.unsubscribe();
    this.approveSub?.unsubscribe();
    this.steerSub?.unsubscribe();
  }

  useExample(example: ExamplePrompt): void {
    this.draft = example.text;
  }

  hasActiveRun(): boolean {
    return !!this.workItemId;
  }

  composerDisabled(): boolean {
    const cmd = this.draft.trim().toUpperCase();
    if (this.workItemId && StudioPageComponent.SteerCommands.has(cmd)) {
      return this.steering();
    }
    return this.busy() && !this.status()?.isPaused;
  }

  send(): void {
    const text = this.draft.trim();
    if (!text) {
      return;
    }

    const steerCmd = text.toUpperCase();
    if (this.workItemId && StudioPageComponent.SteerCommands.has(steerCmd)) {
      this.sendSteer(steerCmd);
      return;
    }

    if (this.busy() && !this.status()?.isPaused) {
      return;
    }

    const example = this.examples.find((e) => e.text === text);
    const body = {
      userRequest: text,
      stackProfile: example?.stackProfile,
      projectName: example?.projectName,
      projectMode: example?.projectMode ?? 'New',
      projectId:
        example?.projectMode === 'Existing' && this.lastProjectId
          ? this.lastProjectId
          : undefined,
      baselineArtifacts: example?.baselineArtifacts,
      simulateTaRouteback: example?.simulateTaRouteback === true,
    };

    this.error.set(null);
    this.busy.set(true);
    this.activeTab = 'chat';
    this.seenEventIds.clear();
    this.pollSub?.unsubscribe();
    this.messages.set([
      {
        id: crypto.randomUUID(),
        role: 'user',
        text,
        at: new Date(),
      },
      {
        id: crypto.randomUUID(),
        role: 'system',
        text: 'Starting the BuildGenix pipeline. Approve at PRD, System Design, Implementation Plan, Code Review, Test Report, and Sandbox. You can type PAUSE, STOP, or ABORT while it runs.',
        at: new Date(),
      },
    ]);
    this.draft = '';
    this.selectedArtifact.set(null);
    this.artifacts.set([]);
    this.logs.set([]);

    this.startSub?.unsubscribe();
    this.startSub = this.api.start(body).subscribe({
      next: (result) => {
        this.workItemId = result.workItemId;
        this.lastProjectId = result.projectId;
        this.pushAgent(result.message, 'INTAKE', 'PipelineStarted');
        this.beginPolling(result.workItemId);
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(err?.error?.error ?? err?.message ?? 'Failed to start pipeline');
      },
    });
  }

  sendSteer(command: string): void {
    if (!this.workItemId || this.steering()) {
      return;
    }
    this.error.set(null);
    this.draft = '';
    this.messages.update((list) => [
      ...list,
      { id: crypto.randomUUID(), role: 'user', text: command, at: new Date() },
    ]);
    this.steering.set(true);
    this.steerSub?.unsubscribe();
    this.steerSub = this.api.steer(this.workItemId, command).subscribe({
      next: (result) => {
        this.steering.set(false);
        this.pushAgent(
          result.message || `${result.command} accepted (${result.status}).`,
          this.status()?.currentStage,
          `Steer${result.command}`,
        );
        if (command === 'ABORT' || result.status === 'Aborted') {
          this.resetToIdle('Work item aborted. Start a new request when ready.');
          return;
        }
        this.refreshAll();
      },
      error: (err) => {
        this.steering.set(false);
        this.error.set(err?.error?.error ?? err?.message ?? 'Steer failed');
      },
    });
  }

  continuePipeline(): void {
    this.sendSteer('CONTINUE');
  }

  approve(): void {
    if (!this.workItemId || this.approving()) {
      return;
    }
    this.approving.set(true);
    this.approveSub?.unsubscribe();
    this.approveSub = this.api.approve(this.workItemId).subscribe({
      next: () => {
        this.approving.set(false);
        this.pushAgent(
          'Approval recorded. Continuing to the next stage…',
          this.status()?.pendingGate ?? undefined,
          'GateApproved',
        );
        this.refreshAll();
      },
      error: (err) => {
        this.approving.set(false);
        this.error.set(err?.error?.error ?? err?.message ?? 'Approve failed');
      },
    });
  }

  setTab(tab: TabId): void {
    this.activeTab = tab;
    if (tab === 'docs') {
      this.refreshArtifacts();
    }
    if (tab === 'logs') {
      this.refreshLogs();
    }
  }

  openArtifact(key: string): void {
    if (!this.workItemId) {
      return;
    }
    this.selectedArtifactKey.set(key);
    this.api.artifact(this.workItemId, key).subscribe({
      next: (doc) => this.selectedArtifact.set(doc),
      error: () => this.selectedArtifact.set(null),
    });
  }

  prettyJson(content: string): string {
    try {
      return JSON.stringify(JSON.parse(content), null, 2);
    } catch {
      return content;
    }
  }

  displayDoc(content: ArtifactContent | null): string {
    if (!content) {
      return '';
    }
    try {
      const parsed = JSON.parse(content.content) as { content?: string };
      if (
        typeof parsed?.content === 'string' &&
        (content.key.endsWith('Md') || content.key.includes('Md'))
      ) {
        return parsed.content;
      }
    } catch {
      // fall through
    }
    return this.prettyJson(content.content);
  }

  private beginPolling(workItemId: string): void {
    this.pollSub?.unsubscribe();
    this.pollSub = interval(1500)
      .pipe(
        switchMap(() =>
          this.api.status(workItemId).pipe(
            catchError((err) => {
              this.error.set(err?.error?.error ?? 'Lost connection to pipeline status');
              return of(null);
            }),
          ),
        ),
      )
      .subscribe((status) => {
        if (!status) {
          return;
        }
        this.status.set(status);
        this.ingestEvents();
        this.refreshArtifacts();
        this.refreshLogs();
        if (status.isPaused) {
          this.busy.set(false);
        }
        if (status.isTerminal) {
          this.busy.set(false);
          this.pollSub?.unsubscribe();
          if (status.status === 'Aborted') {
            this.resetToIdle(status.steerHint ?? 'Aborted.');
            return;
          }
          if (status.failed) {
            this.pushAgent(
              status.errorMessage ?? 'Pipeline failed.',
              status.currentStage,
              'PipelineFailed',
            );
          } else if (status.deployedUrl) {
            this.pushAgent(`Done. App is live at ${status.deployedUrl}`, 'DONE', 'Finalized');
          }
        } else if (!status.isPaused) {
          this.busy.set(true);
        }
      });

    this.api.status(workItemId).subscribe((status) => {
      this.status.set(status);
      this.ingestEvents();
    });
  }

  private ingestEvents(): void {
    if (!this.workItemId) {
      return;
    }
    this.api.events(this.workItemId).subscribe((events) => {
      for (const event of events) {
        if (this.seenEventIds.has(event.eventId)) {
          continue;
        }
        this.seenEventIds.add(event.eventId);
        this.appendEventMessage(event);
      }
      this.scrollChat();
    });
  }

  private appendEventMessage(event: PipelineEvent): void {
    if (event.eventType === 'GateRaised') {
      // WaitingForUser carries the approval CTA; keep GateRaised as progress only.
    }

    const needsApproval = event.eventType === 'WaitingForUser';
    if (
      event.eventType === 'ArtifactReady' &&
      this.messages().some(
        (m) => m.eventType === 'ArtifactReady' && m.artifactKey === event.artifactKey,
      )
    ) {
      return;
    }

    this.messages.update((list) => [
      ...list,
      {
        id: event.eventId,
        role: 'agent',
        text: event.message,
        at: new Date(event.occurredAt),
        stage: event.stage,
        eventType: event.eventType,
        artifactKey: event.artifactKey,
        needsApproval,
        gateStage: needsApproval ? event.stage : null,
        gateLabel: needsApproval ? event.message : null,
      },
    ]);
  }

  private pushAgent(text: string, stage?: string, eventType?: string): void {
    this.messages.update((list) => [
      ...list,
      {
        id: crypto.randomUUID(),
        role: 'agent',
        text,
        at: new Date(),
        stage,
        eventType,
      },
    ]);
    this.scrollChat();
  }

  private refreshAll(): void {
    this.ingestEvents();
    this.refreshArtifacts();
    this.refreshLogs();
    if (this.workItemId) {
      this.api.status(this.workItemId).subscribe((s) => this.status.set(s));
    }
  }

  private refreshArtifacts(): void {
    if (!this.workItemId) {
      return;
    }
    this.api.artifacts(this.workItemId).subscribe({
      next: (items) => this.artifacts.set(items),
      error: () => undefined,
    });
  }

  private refreshLogs(): void {
    if (!this.workItemId) {
      return;
    }
    this.api.logs(this.workItemId).subscribe({
      next: (items) => this.logs.set(items),
      error: () => undefined,
    });
  }

  private scrollChat(): void {
    queueMicrotask(() => {
      const el = this.chatScroll?.nativeElement;
      if (el) {
        el.scrollTop = el.scrollHeight;
      }
    });
  }

  private resetToIdle(message: string): void {
    this.pollSub?.unsubscribe();
    this.workItemId = null;
    this.busy.set(false);
    this.steering.set(false);
    this.status.set(null);
    this.pushAgent(message, undefined, 'Idle');
  }
}
