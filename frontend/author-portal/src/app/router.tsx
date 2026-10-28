import { createBrowserRouter, type RouteObject } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthProvider';
import { RequireAuth } from '../auth/RequireAuth';
import { ArticlePage } from '../pages/ArticlePage';
import { ArticleEditorPage } from '../pages/ArticleEditorPage';
import { FeedArticlePage } from '../pages/FeedArticlePage';
import { FeedPage } from '../pages/FeedPage';
import { HomePage } from '../pages/HomePage';
import { MyArticlesPage } from '../pages/MyArticlesPage';
import { LoginPage } from '../pages/LoginPage';
import { NotFoundPage } from '../pages/NotFoundPage';
import { ProfilePage } from '../pages/ProfilePage';
import { RegisterPage } from '../pages/RegisterPage';
import { RevisionPage } from '../pages/RevisionPage';
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
          { path: 'articles', element: <MyArticlesPage /> },
          { path: 'articles/new', element: <ArticleEditorPage /> },
          { path: 'articles/:id/edit', element: <ArticleEditorPage /> },
          { path: 'articles/:id', element: <ArticlePage /> },
          { path: 'revisions/:id', element: <RevisionPage /> },
          { path: 'feed', element: <FeedPage /> },
          { path: 'feed/:id', element: <FeedArticlePage /> },
          { path: 'profile', element: <ProfilePage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
];

export function createAppRouter() {
  return createBrowserRouter(appRoutes);
}
