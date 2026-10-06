const KEY_PREFIX = 'crm_pwd_prompt_dismissed:';

export function isPasswordPromptDismissed(userId: string): boolean {
  try {
    return sessionStorage.getItem(KEY_PREFIX + userId) === '1';
  } catch {
    return false;
  }
}

export function dismissPasswordPrompt(userId: string): void {
  try {
    sessionStorage.setItem(KEY_PREFIX + userId, '1');
  } catch {
    // storage unavailable: the prompt simply reappears on next load
  }
}

/** Removes every dismissal marker; called on logout so the next login shows the prompt again. */
export function clearPasswordPromptDismissals(): void {
  try {
    const keys: string[] = [];
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i);
      if (key?.startsWith(KEY_PREFIX)) keys.push(key);
    }
    keys.forEach((k) => sessionStorage.removeItem(k));
  } catch {
    // ignore
  }
}
