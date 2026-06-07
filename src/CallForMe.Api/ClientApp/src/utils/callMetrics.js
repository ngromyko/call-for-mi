export function callPrice(config) {
  return Number.isFinite(Number(config?.callPricePerMinute))
    ? Number(config.callPricePerMinute)
    : 0.5;
}

export function hasCallChanged(left, right) {
  if (!left || !right) return !!left !== !!right;
  return left.updatedAt !== right.updatedAt ||
    left.status !== right.status ||
    (left.transcript?.length || 0) !== (right.transcript?.length || 0) ||
    (left.suggestions?.length || 0) !== (right.suggestions?.length || 0);
}

export function wait(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}
