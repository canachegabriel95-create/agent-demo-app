import { randomUUID } from 'node:crypto';
import { test, expect, createInvitation, strongPassword } from '../fixtures';
import { expectProblem } from '../contract';
import { expireInvitation } from '../db';

const newEmail = () => `reg-${randomUUID()}@example.test`;

function form(email: string, code: string, password = strongPassword()) {
  return { invitationCode: code, fullName: 'Test Author', email, password, confirmPassword: password };
}

test.describe('POST /api/registration', () => {
  test('creates an Author and uses the invitation', async ({ request, adminApi }) => {
    const email = newEmail();
    const invitation = await createInvitation(adminApi, email);
    const data = form(email, invitation.code);

    const response = await request.post('/api/registration', { data });
    expect(response.status()).toBe(201);
    expect(await response.json()).toMatchObject({ email, fullName: 'Test Author' });

    const login = await request.post('/api/auth/login', { data: { email, password: data.password, portal: 'Author' } });
    expect(login.status()).toBe(200);
    const updated = await (await adminApi.get(`/api/admin/invitations/${invitation.id}`)).json();
    expect(updated.status).toBe('Used');
  });

  test('ignores a role sent in the body', async ({ request, adminApi }) => {
    const email = newEmail();
    const { code } = await createInvitation(adminApi, email);
    const data = { ...form(email, code), role: 'Administrator' };

    expect((await request.post('/api/registration', { data })).status()).toBe(201);

    const login = await request.post('/api/auth/login', { data: { email, password: data.password, portal: 'Admin' } });
    expect(login.status()).toBe(401);
  });

  test('an email with other case and spaces matches the invitation', async ({ request, adminApi }) => {
    const email = newEmail();
    const { code } = await createInvitation(adminApi, email);
    const data = form(`  ${email.toUpperCase()}  `, code);

    expect((await request.post('/api/registration', { data })).status()).toBe(201);

    const login = await request.post('/api/auth/login', { data: { email, password: data.password, portal: 'Author' } });
    expect(login.status()).toBe(200);
  });

  test('a second account for the same email in other case is rejected', async ({ request, adminApi }) => {
    const email = newEmail();
    const first = await createInvitation(adminApi, email);
    const second = await createInvitation(adminApi, email);
    expect((await request.post('/api/registration', { data: form(email, first.code) })).status()).toBe(201);

    await expectProblem(
      await request.post('/api/registration', { data: form(email.toUpperCase(), second.code) }),
      409,
    );
  });

  test('two parallel requests with one code: exactly one succeeds', async ({ request, adminApi }) => {
    const email = newEmail();
    const { code } = await createInvitation(adminApi, email);

    const responses = await Promise.all([
      request.post('/api/registration', { data: form(email, code) }),
      request.post('/api/registration', { data: form(email, code) }),
    ]);

    expect(responses.map((r) => r.status()).filter((s) => s === 201)).toHaveLength(1);
  });

  test.describe('an unusable invitation', () => {
    async function expectRejected(request: import('@playwright/test').APIRequestContext, email: string, code: string, field: string) {
      const data = form(email, code);
      const body = await expectProblem(await request.post('/api/registration', { data }), 400);
      expect(body.errors[field]).toBeDefined();
      const login = await request.post('/api/auth/login', { data: { email, password: data.password, portal: 'Author' } });
      expect(login.status(), 'no account was created').toBe(401);
    }

    test('unknown code', async ({ request }) => {
      const email = newEmail();
      await expectRejected(request, email, 'AAAA-AAAA-AAAA', 'invitationCode');
    });

    test('email that does not match the invitation', async ({ request, adminApi }) => {
      const { code } = await createInvitation(adminApi, newEmail());
      await expectRejected(request, newEmail(), code, 'email');
    });

    test('expired invitation', async ({ request, adminApi }) => {
      const email = newEmail();
      const { id, code } = await createInvitation(adminApi, email);
      await expireInvitation(id);
      await expectRejected(request, email, code, 'invitationCode');
    });

    test('revoked invitation', async ({ request, adminApi }) => {
      const email = newEmail();
      const { id, code, version } = await createInvitation(adminApi, email);
      await adminApi.post(`/api/admin/invitations/${id}/revoke`, { data: { version } });
      await expectRejected(request, email, code, 'invitationCode');
    });

    test('used invitation', async ({ request, adminApi }) => {
      const email = newEmail();
      const { code } = await createInvitation(adminApi, email);
      await request.post('/api/registration', { data: form(email, code) });
      await expectRejected(request, email, code, 'invitationCode');
    });

    test('no account is created and the invitation stays Available', async ({ request, adminApi }) => {
      const email = newEmail();
      const invitation = await createInvitation(adminApi, email);
      const data = form(email, invitation.code, 'short');

      expect((await request.post('/api/registration', { data })).status()).toBe(400);

      const status = (await (await adminApi.get(`/api/admin/invitations/${invitation.id}`)).json()).status;
      expect(status).toBe('Available');
    });
  });

  test.describe('field validation', () => {
    const cases: [string, Record<string, string>, string][] = [
      ['name of 1 character', { fullName: 'A' }, 'fullName'],
      ['name of 101 characters', { fullName: 'A'.repeat(101) }, 'fullName'],
      ['invalid email', { email: 'not-an-email' }, 'email'],
      ['email over 254 characters', { email: `${'a'.repeat(250)}@example.test` }, 'email'],
      ['password of 11 characters', { password: 'Aa1!aaaaaaa', confirmPassword: 'Aa1!aaaaaaa' }, 'password'],
      ['password without upper case', { password: 'aa1!aaaaaaaaaa', confirmPassword: 'aa1!aaaaaaaaaa' }, 'password'],
      ['password without lower case', { password: 'AA1!AAAAAAAAAA', confirmPassword: 'AA1!AAAAAAAAAA' }, 'password'],
      ['password without digit', { password: 'Aa!!aaaaaaaaaa', confirmPassword: 'Aa!!aaaaaaaaaa' }, 'password'],
      ['password without symbol', { password: 'Aa1aaaaaaaaaaa', confirmPassword: 'Aa1aaaaaaaaaaa' }, 'password'],
      ['password over 128 characters', { password: `Aa1!${'a'.repeat(125)}`, confirmPassword: `Aa1!${'a'.repeat(125)}` }, 'password'],
      ['different confirmation', { confirmPassword: 'Different-1234!' }, 'confirmPassword'],
    ];
    for (const [name, change, field] of cases) {
      test(`${name} returns 400 on ${field}`, async ({ request }) => {
        const data = { ...form(newEmail(), 'AAAA-AAAA-AAAA'), ...change };
        const body = await expectProblem(await request.post('/api/registration', { data }), 400);
        expect(body.errors[field]).toBeDefined();
      });
    }

    test('an empty body reports every required field', async ({ request }) => {
      const body = await expectProblem(await request.post('/api/registration', { data: {} }), 400);
      expect(Object.keys(body.errors).sort()).toEqual(['confirmPassword', 'email', 'fullName', 'invitationCode', 'password']);
    });
  });
});

test.describe('POST /api/registration/validate-invitation', () => {
  test('a valid code and email return valid and do not use the code', async ({ request, adminApi }) => {
    const email = newEmail();
    const { code } = await createInvitation(adminApi, email);

    const check = await request.post('/api/registration/validate-invitation', { data: { code, email } });
    expect(await check.json()).toEqual({ valid: true });

    expect((await request.post('/api/registration', { data: form(email, code) })).status()).toBe(201);
  });

  test('an unknown code returns 400 with reason NotFound', async ({ request }) => {
    const body = await expectProblem(
      await request.post('/api/registration/validate-invitation', { data: { code: 'AAAA-AAAA-AAAA', email: newEmail() } }),
      400,
    );
    expect(body.reason).toBe('NotFound');
  });

  test('another email returns 400 with reason EmailMismatch', async ({ request, adminApi }) => {
    const { code } = await createInvitation(adminApi, newEmail());
    const body = await expectProblem(
      await request.post('/api/registration/validate-invitation', { data: { code, email: newEmail() } }),
      400,
    );
    expect(body.reason).toBe('EmailMismatch');
  });

  test('missing fields return 400 with errors', async ({ request }) => {
    const body = await expectProblem(await request.post('/api/registration/validate-invitation', { data: {} }), 400);
    expect(Object.keys(body.errors).sort()).toEqual(['code', 'email']);
  });
});
