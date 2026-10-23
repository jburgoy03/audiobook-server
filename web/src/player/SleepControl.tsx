import { SLEEP_MINUTES, sleepStore, useSleepTimer } from './sleepTimer'
import { formatTime } from './timeline'

/**
 * The sleep timer, beside Speed and styled like it: a real <select>, so phones
 * get their own picker (the wheel on an iPhone) and the keyboard works. While
 * minutes run, the select shows the countdown through an extra option that
 * can't be picked; choosing anything else replaces the timer.
 */
export function SleepControl() {
  const timer = useSleepTimer()
  const value = timer.kind === 'minutes' ? 'running' : timer.kind

  const choose = (choice: string) => {
    if (choice === 'off') sleepStore.off()
    else if (choice === 'chapter') sleepStore.set({ kind: 'chapter' })
    else if (choice !== 'running') sleepStore.set({ kind: 'minutes', remaining: Number(choice) * 60 })
  }

  return (
    <label className="sleep picker">
      <span>Sleep</span>
      <select value={value} onChange={(e) => choose(e.target.value)}>
        {timer.kind === 'minutes' && (
          <option value="running" disabled hidden>
            {/* Rounded up, so it reads 0:01 until the moment it stops, not 0:00. */}
            {formatTime(Math.ceil(timer.remaining))}
          </option>
        )}
        <option value="off">Off</option>
        {SLEEP_MINUTES.map((m) => (
          <option key={m} value={m}>
            {m} min
          </option>
        ))}
        <option value="chapter">End of chapter</option>
      </select>
    </label>
  )
}
