import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ArtifactContent,
  ArtifactListItem,
  LogEntry,
  PipelineEvent,
  PipelineStatus,
  StartResult,
  SteerResult,
} from './pipeline.models';

@Injectable({ providedIn: 'root' })
export class PipelineApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/poc/pipeline`;

  start(body: {
    userRequest: string;
    stackProfile?: string;
    projectMode?: string;
    projectName?: string;
    projectId?: string;
  }): Observable<StartResult> {
    return this.http.post<StartResult>(`${this.base}/start`, body, {
      headers: { 'X-User-Id': 'dummy-user' },
    });
  }

  status(workItemId: string): Observable<PipelineStatus> {
    return this.http.get<PipelineStatus>(`${this.base}/${workItemId}/status`);
  }

  events(workItemId: string): Observable<PipelineEvent[]> {
    return this.http.get<PipelineEvent[]>(`${this.base}/${workItemId}/events`);
  }

  artifacts(workItemId: string): Observable<ArtifactListItem[]> {
    return this.http.get<ArtifactListItem[]>(`${this.base}/${workItemId}/artifacts`);
  }

  artifact(workItemId: string, key: string): Observable<ArtifactContent> {
    return this.http.get<ArtifactContent>(`${this.base}/${workItemId}/artifacts/${encodeURIComponent(key)}`);
  }

  logs(workItemId: string): Observable<LogEntry[]> {
    return this.http.get<LogEntry[]>(`${this.base}/${workItemId}/logs`);
  }

  approve(workItemId: string, feedback?: string): Observable<void> {
    return this.http.post<void>(
      `${this.base}/${workItemId}/approve`,
      { feedback: feedback ?? null },
      { headers: { 'X-User-Id': 'dummy-user' } },
    );
  }

  steer(workItemId: string, command: string): Observable<SteerResult> {
    return this.http.post<SteerResult>(
      `${this.base}/${workItemId}/steer`,
      { command },
      { headers: { 'X-User-Id': 'dummy-user' } },
    );
  }
}
