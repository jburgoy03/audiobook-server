package dev.deanburgoyne.audiobooks.playback

import dev.deanburgoyne.audiobooks.api.BookFile
import dev.deanburgoyne.audiobooks.api.Chapter
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class TimelineTest {
    // Three files: [0, 100), [100, 250), [250, 300).
    private val files = listOf(
        BookFile(0, 0.0, 100.0, "audio/mpeg", 1),
        BookFile(1, 100.0, 150.0, "audio/mpeg", 1),
        BookFile(2, 250.0, 50.0, "audio/mpeg", 1),
    )

    private val chapters = listOf(
        Chapter(0, "One", 0.0, 120.0),
        Chapter(1, "Two", 120.0, 260.0),
        Chapter(2, "Three", 260.0, 300.0),
    )

    @Test
    fun a_position_resolves_to_its_file() {
        assertEquals(FileLocation(0, 0.0), files.locate(0.0))
        assertEquals(FileLocation(1, 50.0), files.locate(150.0))
        assertEquals(FileLocation(2, 10.0), files.locate(260.0))
    }

    @Test
    fun a_boundary_belongs_to_the_file_that_starts_there() {
        assertEquals(FileLocation(1, 0.0), files.locate(100.0))
        assertEquals(FileLocation(2, 0.0), files.locate(250.0))
    }

    @Test
    fun out_of_range_clamps_rather_than_failing() {
        assertEquals(FileLocation(0, 0.0), files.locate(-5.0))
        assertEquals(FileLocation(2, 50.0), files.locate(999.0))
        assertEquals(FileLocation(0, 0.0), files.locate(Double.NaN))
        assertEquals(FileLocation(0, 0.0), emptyList<BookFile>().locate(10.0))
    }

    @Test
    fun file_positions_map_back_to_the_book() {
        for (t in listOf(0.0, 99.5, 100.0, 180.0, 250.0, 299.0)) {
            val (index, offset) = files.locate(t)
            assertEquals(t, files.toBookPosition(index, offset))
        }
        assertEquals(300.0, files.toBookPosition(2, 999.0)) // a player overshooting the file's end
    }

    @Test
    fun chapters_and_their_navigation() {
        assertEquals(1, chapters.indexAt(120.0))
        assertEquals(-1, emptyList<Chapter>().indexAt(5.0))

        assertEquals(120.0, chapters.previousTarget(200.0)) // well into "Two": its start
        assertEquals(0.0, chapters.previousTarget(121.0))   // just started "Two": back to "One"
        assertEquals(0.0, chapters.previousTarget(1.0))     // first chapter: its start

        assertEquals(260.0, chapters.nextTarget(200.0))
        assertNull(chapters.nextTarget(270.0))
    }

    @Test
    fun clock_format() {
        assertEquals("0:00", formatClock(0.0))
        assertEquals("2:05", formatClock(125.9))
        assertEquals("1:02:05", formatClock(3725.0))
    }
}
