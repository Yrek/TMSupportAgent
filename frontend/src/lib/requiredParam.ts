export function requiredParam(
  value: string | undefined,
  name: string,
): string {
  if (!value) {
    throw new Error(`Missing required route param: ${name}`);
  }
  return value;
}

