plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.serialization")
}

android {
    namespace = "com.crossdeck.client"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.crossdeck.client"
        minSdk = 31 // Android 12+, per architecture spec decision #13
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0-milestone1"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
    }

    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.14"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.3")
    implementation("androidx.activity:activity-compose:1.9.0")

    implementation(platform("androidx.compose:compose-bom:2024.06.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")

    // WebSocket client
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    // JSON serialization matching shared-schema/protocol.md
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.6.3")

    // CameraX + ML Kit Barcode Scanning for QR Code pairing
    implementation("androidx.camera:camera-camera2:1.3.1")
    implementation("androidx.camera:camera-lifecycle:1.3.1")
    implementation("androidx.camera:camera-view:1.3.1")
    implementation("com.google.mlkit:barcode-scanning:17.2.0")

    debugImplementation("androidx.compose.ui:ui-tooling")
}
