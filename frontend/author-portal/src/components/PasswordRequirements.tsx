import { passwordRequirements } from '../validation/passwordPolicy';
import styles from './PasswordRequirements.module.css';

export interface PasswordRequirementsProps {
  password: string;
  /** Lets a field reference the list with `aria-describedby`. */
  id?: string;
}

/**
 * A live checklist of the password rules. Each item says "Met" or "Not met" in words (the icon and colour
 * only reinforce it) and changes are announced politely.
 */
export function PasswordRequirements({ password, id }: PasswordRequirementsProps) {
  return (
    <div className={styles.root} id={id}>
      <p className={styles.heading}>Your password must:</p>
      <ul className={styles.list} aria-live="polite" data-testid="password-requirements">
        {passwordRequirements(password).map((requirement) => (
          <li
            key={requirement.id}
            className={requirement.met ? styles.met : styles.unmet}
            data-requirement={requirement.id}
            data-met={requirement.met}
          >
            <span aria-hidden="true" className={styles.icon}>
              {requirement.met ? '✓' : '○'}
            </span>
            <span>{requirement.text}</span>
            <span className={styles.status}>{requirement.met ? ' Met.' : ' Not met.'}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
