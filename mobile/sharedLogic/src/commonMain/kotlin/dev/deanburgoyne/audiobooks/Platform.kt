package dev.deanburgoyne.audiobooks

interface Platform {
    val name: String
}

expect fun getPlatform(): Platform