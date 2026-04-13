import { AgentExecutionStepDto, PreviewTimelineItem } from '../../types/agent';

export const mapStepToTimeline = (step: AgentExecutionStepDto): PreviewTimelineItem => ({
  id: step.id,
  type: step.stepType,
  toolName: step.toolName,
  durationMs: step.durationMs,
  tokensUsed: step.tokensUsed,
  inputJson: step.inputJson,
  outputJson: step.outputJson,
  llmPrompt: step.llmPrompt,
  llmResponse: step.llmResponse,
  isSuccess: step.isSuccess,
  errorMessage: step.errorMessage
});

export const computeTotalTokens = (items: PreviewTimelineItem[]): number =>
  items.reduce((acc, item) => acc + (item.tokensUsed ?? 0), 0);
