import { configureStore } from '@reduxjs/toolkit';

import toolsReducer from '../pages/tools/Redux/Slice';
import modelsReducer from '../pages/models/Redux/Slice';
import agentsReducer from '../pages/agents/Redux/Slice';
import overviewReducer from '../pages/overview/overviewSlice';
import executionsReducer from '../pages/executions/Redux/Slice';
import designerReducer from '../pages/agents/Designer/designerSlice';
import checkpointReducer from '../pages/checkpoints/checkpointSlice';
import executionDetailReducer from '../pages/executions/Detail/executionDetailSlice';

export const store = configureStore({
  reducer: {
    agents: agentsReducer,
    executions: executionsReducer,
    tools: toolsReducer,
    models: modelsReducer,
    designer: designerReducer,
    overview: overviewReducer,
    checkpoints: checkpointReducer,
    executionDetail: executionDetailReducer,
  },
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
