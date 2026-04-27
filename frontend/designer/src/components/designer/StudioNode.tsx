import { Handle, NodeProps, Position } from '@xyflow/react';
import { AgentNodeData } from '../../types/agent';

export const StudioNode = ({ data, selected }: NodeProps) => {
  const nodeData = data as AgentNodeData;
  const transitions = Array.isArray(nodeData.config?.transitions)
    ? (nodeData.config?.transitions as string[])
    : ['default'];

  return (
    <div className={`studio-node ${selected ? 'selected' : ''}`}>
      <Handle type="target" position={Position.Left} id="in" />
      <div className="title">{String(nodeData.label ?? 'Untitled Step')}</div>
      <div className="meta">{String(nodeData.type ?? 'think').toUpperCase()}</div>
      <div className="transitions">
        {transitions.map((transition) => (
          <span key={transition} className="chip">
            {transition}
          </span>
        ))}
      </div>
      <Handle type="source" position={Position.Right} id="out" />
    </div>
  );
};
