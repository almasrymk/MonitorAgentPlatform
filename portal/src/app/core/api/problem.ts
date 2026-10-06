import { HttpErrorResponse } from '@angular/common/http';

import { Problem } from './models';

/** Reads the problem details of an HTTP error; non-problem errors get a generic code. */
export function toProblem(error: unknown): Problem {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as Partial<Problem> | null;
    if (body && typeof body === 'object' && typeof body.code === 'string') {
      return { ...body, status: error.status, code: body.code } as Problem;
    }
    if (error.status === 0) {
      return { status: 0, code: 'NETWORK_ERROR' };
    }
    return { status: error.status, code: error.status >= 500 ? 'INTERNAL_ERROR' : 'ERROR' };
  }
  return { status: 0, code: 'ERROR' };
}
