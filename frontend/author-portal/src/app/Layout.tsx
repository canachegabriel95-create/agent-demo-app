import { NavLink, Outlet } from 'react-router-dom';
import { APP_NAME } from './config';
import styles from './Layout.module.css';

export function Layout() {
  return (
    <>
      <a className={styles.skipLink} href="#main-content">
        Skip to main content
      </a>
      <header className={styles.header}>
        <p className={styles.brand}>{APP_NAME}</p>
        {/* Placeholder navigation: later stories add the real sections. */}
        <nav className={styles.nav} aria-label="Main">
          <NavLink to="/" end>
            Home
          </NavLink>
          <NavLink to="/login">Sign in</NavLink>
        </nav>
      </header>
      <main id="main-content" className={styles.main} tabIndex={-1}>
        <Outlet />
      </main>
    </>
  );
}
