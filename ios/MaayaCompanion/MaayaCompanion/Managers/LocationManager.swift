import Foundation
import CoreLocation

@Observable
final class LocationManager: NSObject {
    var currentLocation: CLLocation?
    var currentAddress: String?
    var authorizationStatus: CLAuthorizationStatus = .notDetermined
    var errorMessage: String?

    private let manager = CLLocationManager()
    private let geocoder = CLGeocoder()

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyBest
        manager.allowsBackgroundLocationUpdates = true
        manager.pausesLocationUpdatesAutomatically = false
    }

    func requestAuthorization() {
        manager.requestAlwaysAuthorization()
    }

    func startMonitoring() {
        guard authorizationStatus == .authorizedAlways || authorizationStatus == .authorizedWhenInUse else {
            errorMessage = "Location access not granted. Go to Settings > Privacy > Location Services to enable."
            return
        }
        manager.startMonitoringSignificantLocationChanges()
    }

    func stopMonitoring() {
        manager.stopMonitoringSignificantLocationChanges()
    }

    func requestCurrentLocation() {
        manager.requestLocation()
    }

    private func reverseGeocode(_ location: CLLocation) {
        geocoder.reverseGeocodeLocation(location) { [weak self] placemarks, error in
            guard let self else { return }
            if let error {
                self.errorMessage = "Geocoding failed: \(error.localizedDescription)"
                return
            }
            if let placemark = placemarks?.first {
                let components = [
                    placemark.name,
                    placemark.locality,
                    placemark.administrativeArea
                ].compactMap { $0 }
                self.currentAddress = components.joined(separator: ", ")
            }
        }
    }

    func toPayload() -> LocationPayload? {
        guard let location = currentLocation else { return nil }
        return LocationPayload(
            latitude: location.coordinate.latitude,
            longitude: location.coordinate.longitude,
            address: currentAddress
        )
    }
}

extension LocationManager: CLLocationManagerDelegate {
    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let location = locations.last else { return }
        currentLocation = location
        errorMessage = nil
        reverseGeocode(location)
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        errorMessage = "Location error: \(error.localizedDescription)"
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        authorizationStatus = manager.authorizationStatus
        if authorizationStatus == .authorizedAlways || authorizationStatus == .authorizedWhenInUse {
            startMonitoring()
        }
    }
}
