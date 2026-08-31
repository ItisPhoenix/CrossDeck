package com.crossdeck.client.connection

import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.X509TrustManager

/** Trust manager for the host's self-signed certificate fingerprint delivered during pairing. */
internal class PinnedCertificateTrustManager(expectedFingerprint: String) : X509TrustManager {
    private val expected = PairingSecurity.normalizeFingerprint(expectedFingerprint)
        ?: throw IllegalArgumentException("Invalid host certificate fingerprint")

    override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        throw CertificateException("Client certificates are not accepted")
    }

    override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        if (chain.isNullOrEmpty()) throw CertificateException("Host did not provide a certificate")
        val certificate = chain[0]
        try {
            certificate.checkValidity()
        } catch (e: Exception) {
            throw CertificateException("Host certificate is not currently valid", e)
        }
        if (!matches(certificate)) {
            throw CertificateException("Host certificate fingerprint does not match the paired host")
        }
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()

    fun matches(certificate: java.security.cert.Certificate): Boolean {
        val actual = MessageDigest.getInstance("SHA-256")
            .digest(certificate.encoded)
            .joinToString("") { "%02X".format(it.toInt() and 0xFF) }
        return actual == expected
    }
}

internal object PairingSecurity {
    fun normalizeFingerprint(raw: String): String? {
        val normalized = raw.trim()
            .replace(":", "")
            .replace(" ", "")
            .uppercase()
        return if (normalized.length == 64 && normalized.all { it in "0123456789ABCDEF" }) normalized else null
    }

    fun normalizeIpv4(raw: String): String? {
        val parts = raw.trim().split('.')
        if (parts.size != 4) return null
        if (parts.any { it.isEmpty() || (it.length > 1 && it.startsWith('0')) }) return null
        if (parts.any { part -> part.toIntOrNull()?.let { it !in 0..255 } != false }) return null
        val normalized = parts.joinToString(".") { it.toInt().toString() }
        if (normalized == "0.0.0.0" || normalized.startsWith("127.") || normalized.startsWith("169.254.")) return null
        return normalized
    }

    fun isValidPort(port: Int): Boolean = port in 1..65535

    fun isValidPin(pin: String): Boolean = pin.length == 6 && pin.all { it in '0'..'9' }

    fun isValidSha256Hex(value: String): Boolean =
        value.length == 64 && value.all { it in "0123456789abcdefABCDEF" }

    fun securityCode(raw: String): String? {
        val normalized = normalizeFingerprint(raw) ?: return null
        return listOf(
            normalized.substring(0, 4),
            normalized.substring(4, 8),
            normalized.substring(56, 60),
            normalized.substring(60, 64)
        ).joinToString("-")
    }
}
