package dev.deanburgoyne.audiobooks.playback

import dev.deanburgoyne.audiobooks.api.Chapter
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNull

class SleepClockTest {
    private val chapters = listOf(
        Chapter(0, "One", 0.0, 120.0),
        Chapter(1, "Two", 120.0, 260.0),
        Chapter(2, "Three", 260.0, 300.0),
    )

    @Test
    fun minutes_count_down_while_playing_then_fade_then_sleep_a_step_back() {
        val clock = SleepClock()
        clock.set(SleepTimer.After(1))
        assertEquals(SleepAction.None, clock.tick(0.0, 0.0, 1f, chapters))
        assertEquals(SleepAction.None, clock.tick(45.0, 45.0, 1f, chapters))
        assertEquals(15.0, clock.remainingSeconds)

        val fading = clock.tick(55.0, 55.0, 1f, chapters)
        assertIs<SleepAction.Fade>(fading)
        assertEquals(0.25f, fading.volume, 0.001f)

        // Stops, and steps back over the 10 s fade.
        assertEquals(SleepAction.Sleep(50.0), clock.tick(60.0, 60.0, 1f, chapters))
        assertNull(clock.timer)
        assertNull(clock.remainingSeconds)
    }

    @Test
    fun a_pause_keeps_the_time_left() {
        val clock = SleepClock()
        clock.set(SleepTimer.After(1))
        clock.tick(0.0, 0.0, 1f, chapters)
        clock.tick(20.0, 20.0, 1f, chapters)
        clock.interrupt() // paused for ten minutes
        clock.tick(620.0, 20.0, 1f, chapters)
        clock.tick(630.0, 30.0, 1f, chapters)
        assertEquals(30.0, clock.remainingSeconds)
    }

    @Test
    fun the_step_back_is_in_book_seconds() {
        val clock = SleepClock()
        clock.set(SleepTimer.After(5))
        clock.tick(0.0, 100.0, 2f, chapters)
        assertEquals(SleepAction.Sleep(80.0), clock.tick(300.0, 700.0, 2f, chapters))
    }

    @Test
    fun end_of_chapter_parks_on_the_boundary() {
        val clock = SleepClock()
        clock.set(SleepTimer.EndOfChapter)
        assertEquals(SleepAction.None, clock.tick(0.0, 119.6, 1f, chapters))
        assertEquals(SleepAction.Sleep(120.0), clock.tick(0.5, 120.1, 1f, chapters))
        assertNull(clock.timer)
    }

    @Test
    fun end_of_chapter_follows_a_skip_into_the_next_chapter() {
        val clock = SleepClock()
        clock.set(SleepTimer.EndOfChapter)
        clock.tick(0.0, 100.0, 1f, chapters)
        // Next chapter: a seek, not a crossing.
        assertEquals(SleepAction.None, clock.tick(0.5, 120.0, 1f, chapters))
        // A scrub to near its end, then playback runs out of chapter two.
        assertEquals(SleepAction.None, clock.tick(1.0, 259.7, 1f, chapters))
        assertEquals(SleepAction.Sleep(260.0), clock.tick(1.5, 260.2, 1f, chapters))
    }

    @Test
    fun a_crossing_needs_playback_not_a_seek() {
        assertEquals(120.0, chapters.crossedBoundary(119.8, 120.05, 0.25))
        assertEquals(120.0, chapters.crossedBoundary(119.6, 120.1, 0.5)) // 2× speed
        assertNull(chapters.crossedBoundary(100.0, 130.0, 0.5)) // a 30 s skip
        assertNull(chapters.crossedBoundary(121.0, 119.0, 0.5)) // backwards
        assertNull(chapters.crossedBoundary(100.0, 100.5, 0.5)) // within a chapter
        assertNull(emptyList<Chapter>().crossedBoundary(10.0, 10.5, 0.5))
    }

    @Test
    fun off_does_nothing() {
        val clock = SleepClock()
        clock.tick(0.0, 119.6, 1f, chapters)
        assertEquals(SleepAction.None, clock.tick(0.5, 120.1, 1f, chapters))
    }
}
