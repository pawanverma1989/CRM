import { setupServer } from 'msw/node';
import { handlers } from './handlers';

/** The MSW server shared by every test file; handlers are added to as the test suite grows. */
export const server = setupServer(...handlers);
