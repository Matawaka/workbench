export function brierScore(samples) {
  if (!Array.isArray(samples) || samples.length === 0) throw new Error("samples are required.");
  return samples.reduce((sum, s) => sum + (s.probability - s.outcome) ** 2, 0) / samples.length;
}

export function expectedCalibrationError(samples, bins = 10) {
  if (!Array.isArray(samples) || samples.length === 0) throw new Error("samples are required.");
  const buckets = Array.from({ length: bins }, () => []);
  for (const sample of samples) {
    if (sample.probability < 0 || sample.probability > 1) throw new Error("probability outside [0,1].");
    if (sample.outcome !== 0 && sample.outcome !== 1) throw new Error("outcome must be 0 or 1.");
    const index = Math.min(bins - 1, Math.floor(sample.probability * bins));
    buckets[index].push(sample);
  }

  let ece = 0;
  const report = [];
  for (let i = 0; i < buckets.length; i += 1) {
    const bucket = buckets[i];
    if (bucket.length === 0) continue;
    const meanProbability = bucket.reduce((s, x) => s + x.probability, 0) / bucket.length;
    const accuracy = bucket.reduce((s, x) => s + x.outcome, 0) / bucket.length;
    const weight = bucket.length / samples.length;
    ece += weight * Math.abs(meanProbability - accuracy);
    report.push({ bin: i, count: bucket.length, meanProbability, accuracy });
  }
  return { ece, bins: report };
}
