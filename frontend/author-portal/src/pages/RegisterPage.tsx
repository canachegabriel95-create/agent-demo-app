import { Link } from 'react-router-dom';

/** Placeholder: the registration form is delivered by story F05.3. */
export function RegisterPage() {
  return (
    <>
      <h1>Create an account</h1>
      <p>Registration with an invitation code will be available soon.</p>
      <p>
        <Link to="/login">Back to sign in</Link>
      </p>
    </>
  );
}
