import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { APP_NAME } from './config';
import styles from './Layout.module.css';

export function Layout() {
  const { status, user, signOut } = useAuth();
  const navigate = useNavigate();

  async function handleLogout() {
    // signOut never rejects: the local session ends even when the API call fails.
    await signOut();
    navigate('/login', { replace: true });
  }

  return (
    <>
      <a className={styles.skipLink} href="#main-content">
        Skip to main content
      </a>
      <header className={styles.header}>
        <p className={styles.brand}>{APP_NAME}</p>
        {/* Later stories add the other sections. */}
        <nav className={styles.nav} aria-label="Main">
          {status === 'authenticated' ? (
            <>
              <NavLink to="/" end>
                Home
              </NavLink>
              <NavLink to="/reviews">Review queue</NavLink>
              <NavLink to="/users">Authors</NavLink>
              <NavLink to="/invitations">Invitations</NavLink>
            </>
          ) : (
            <NavLink to="/login">Sign in</NavLink>
          )}
        </nav>
        {status === 'authenticated' && user && (
          <div className={styles.account}>
            <span data-testid="current-user">{user.fullName}</span>
            <button
              type="button"
              className={styles.logout}
              onClick={handleLogout}
              data-testid="logout-button"
            >
              Log out
            </button>
          </div>
        )}
      </header>
      <main id="main-content" className={styles.main} tabIndex={-1}>
        <Outlet />
      </main>
    </>
  );
}
