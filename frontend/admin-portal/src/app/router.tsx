import { createBrowserRouter, type RouteObject } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthProvider';
import { RequireAuth } from '../auth/RequireAuth';
import { AuditPage } from '../pages/AuditPage';
import { HomePage } from '../pages/HomePage';
import { InvitationsPage } from '../pages/InvitationsPage';
import { LoginPage } from '../pages/LoginPage';
import { NotFoundPage } from '../pages/NotFoundPage';
import { ReviewPage } from '../pages/ReviewPage';
import { ReviewQueuePage } from '../pages/ReviewQueuePage';
import { RevisionReviewPage } from '../pages/RevisionReviewPage';
import { UserDetailPage } from '../pages/UserDetailPage';
import { UsersPage } from '../pages/UsersPage';
import { Layout } from './Layout';

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: (
      <AuthProvider>
        <Layout />
      </AuthProvider>
    ),
    children: [
      { path: 'login', element: <LoginPage /> },
      {
        // Everything else needs a signed-in administrator, including unknown paths.
        element: <RequireAuth />,
        children: [
          { index: true, element: <HomePage /> },
          { path: 'invitations', element: <InvitationsPage /> },
          { path: 'audit', element: <AuditPage /> },
          { path: 'reviews', element: <ReviewQueuePage /> },
          { path: 'reviews/revisions/:revisionId', element: <RevisionReviewPage /> },
          { path: 'reviews/:articleId', element: <ReviewPage /> },
          { path: 'users', element: <UsersPage /> },
          { path: 'users/:id', element: <UserDetailPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
];

export function createAppRouter() {
  return createBrowserRouter(appRoutes);
}
