require 'json'

package = JSON.parse(File.read(File.join(__dir__, 'package.json')))

# Only used if the Xcode project was created with CocoaPods; Swift Package Manager uses Package.swift.
Pod::Spec.new do |s|
  s.name = 'VitaraHealth'
  s.version = package['version']
  s.summary = package['description']
  s.license = 'UNLICENSED'
  s.homepage = 'https://vitara.app'
  s.author = 'Vitara'
  s.source = { :path => '.' }
  s.source_files = 'ios/Sources/**/*.swift'
  s.ios.deployment_target = '15.0'
  s.frameworks = 'HealthKit'
  s.dependency 'Capacitor'
  s.swift_version = '5.1'
end
