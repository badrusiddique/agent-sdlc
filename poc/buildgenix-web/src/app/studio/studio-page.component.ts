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
  projectMode: 'New' | 'Existing';
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
  ];

  draft = '';
  activeTab: TabId = 'chat';
  busy = signal(false);
  approving = signal(false);
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
  private seenEventIds = new Set<string>();
  private workItemId: string | null = null;

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.startSub?.unsubscribe();
    this.approveSub?.unsubscribe();
  }

  useExample(example: ExamplePrompt): void {
    this.draft = example.text;
  }

  send(): void {
    const text = this.draft.trim();
    if (!text || this.busy()) {
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
        text: 'Starting the BuildGenix pipeline. You will see stage updates here, and you must approve PRD, System Design, and Deploy before the run finishes.',
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
        if (status.isTerminal) {
          this.busy.set(false);
          this.pollSub?.unsubscribe();
          if (status.failed) {
            this.pushAgent(
              status.errorMessage ?? 'Pipeline failed.',
              status.currentStage,
              'PipelineFailed',
            );
          } else if (status.deployedUrl) {
            this.pushAgent(`Done. App is live at ${status.deployedUrl}`, 'DONE', 'Finalized');
          }
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
}
