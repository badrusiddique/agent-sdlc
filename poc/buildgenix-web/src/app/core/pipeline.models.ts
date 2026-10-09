export interface StageStripItem {
  id: string;
  label: string;
  state: 'pending' | 'active' | 'running' | 'awaiting' | 'done' | 'failed' | string;
}

export interface PipelineStatus {
  projectId: string;
  workItemId: string;
  status: string;
  currentStage: string;
  pendingGate: string | null;
  pendingGateLabel: string | null;
  deployedUrl: string | null;
  userRequest: string;
  stackProfile: string;
  projectName: string;
  isTerminal: boolean;
  failed: boolean;
  errorMessage: string | null;
  stages: StageStripItem[];
  updatedAt: string;
}

export interface PipelineEvent {
  eventId: string;
  stage: string;
  status: string;
  eventType: string;
  message: string;
  taskId: string | null;
  occurredAt: string;
  artifactKey: string | null;
}

export interface ArtifactListItem {
  key: string;
  revision: number;
  blobPath: string;
  title: string | null;
}

export interface ArtifactContent {
  key: string;
  blobPath: string;
  contentType: string;
  content: string;
}

export interface LogEntry {
  at: string;
  level: string;
  stage: string;
  source: string;
  message: string;
}

export interface StartResult {
  projectId: string;
  workItemId: string;
  status: string;
  stage: string;
  message: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'agent' | 'system';
  text: string;
  at: Date;
  stage?: string;
  eventType?: string;
  artifactKey?: string | null;
  needsApproval?: boolean;
  gateStage?: string | null;
  gateLabel?: string | null;
}
