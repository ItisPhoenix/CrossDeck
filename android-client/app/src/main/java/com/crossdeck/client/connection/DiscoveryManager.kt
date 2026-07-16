package com.crossdeck.client.connection

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo

/**
 * Milestone 2: wire this into PairingScreen so found PCs show as tappable options instead of
 * requiring manual IP entry. Windows Host side needs a matching mDNS broadcast (advertising
 * "_appname._tcp" — implement with a library like Zeroconf/Bonjour SDK or a raw mDNS responder;
 * not yet implemented on the Host in Milestone 1).
 *
 * Stubbed out now so the shape exists, but NOT called from anywhere yet — manual IP+PIN entry
 * (ConnectionManager.connectWithPin) is the only path in Milestone 1.
 */
class DiscoveryManager(context: Context) {

    private val nsdManager = context.getSystemService(Context.NSD_SERVICE) as NsdManager
    private val serviceType = "_appname._tcp." // TODO: match whatever the Host actually broadcasts once implemented

    fun startDiscovery(onFound: (host: String, port: Int) -> Unit) {
        val listener = object : NsdManager.DiscoveryListener {
            override fun onDiscoveryStarted(serviceType: String) {}
            override fun onDiscoveryStopped(serviceType: String) {}
            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {}
            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}

            override fun onServiceFound(service: NsdServiceInfo) {
                nsdManager.resolveService(service, object : NsdManager.ResolveListener {
                    override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {}
                    override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                        val host = serviceInfo.host?.hostAddress ?: return
                        onFound(host, serviceInfo.port)
                    }
                })
            }

            override fun onServiceLost(service: NsdServiceInfo) {}
        }

        nsdManager.discoverServices(serviceType, NsdManager.PROTOCOL_DNS_SD, listener)
    }
}
