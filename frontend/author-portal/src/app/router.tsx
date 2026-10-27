import { createBrowserRouter, type RouteObject } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthProvider';
import { RequireAuth } from '../auth/RequireAuth';
import { ComingSoonPage } from '../pages/ComingSoonPage';
import { HomePage } from '../pages/HomePage';
import { LoginPage } from '../pages/LoginPage';
import { NotFoundPage } from '../pages/NotFoundPage';
import { RegisterPage } from '../pages/RegisterPage';
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
      // Public: an author without an account must reach these.
      { path: 'login', element: <LoginPage /> },
      { path: 'register', element: <RegisterPage /> },
      {
        // Everything else needs a signed-in author, including unknown paths.
        element: <RequireAuth />,
        children: [
          { index: true, element: <HomePage /> },
          { path: 'articles', element: <ComingSoonPage title="My Articles" /> },
          { path: 'feed', element: <ComingSoonPage title="Feed" /> },
          { path: 'profile', element: <ComingSoonPage title="Profile" /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
];

export function createAppRouter() {
  return createBrowserRouter(appRoutes);
}
