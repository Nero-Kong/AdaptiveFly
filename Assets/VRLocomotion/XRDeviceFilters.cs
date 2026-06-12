using UnityEngine.XR;

public static class XRDeviceFilters
{
    public static bool IsLikelyBodyTracker(InputDevice device)
    {
        string descriptor = DeviceDescriptor(device);
        return descriptor.Contains("tracker") ||
            descriptor.Contains("ultimate") ||
            descriptor.Contains("body") ||
            descriptor.Contains("waist") ||
            descriptor.Contains("hip") ||
            descriptor.Contains("pelvis") ||
            descriptor.Contains("foot") ||
            descriptor.Contains("tundra") ||
            descriptor.Contains("tracking reference") ||
            descriptor.Contains("base station");
    }

    public static bool IsHandController(InputDevice device, bool leftHand)
    {
        if (!device.isValid)
        {
            return false;
        }

        InputDeviceCharacteristics handCharacteristic = leftHand
            ? InputDeviceCharacteristics.Left
            : InputDeviceCharacteristics.Right;
        InputDeviceCharacteristics required =
            InputDeviceCharacteristics.Controller |
            InputDeviceCharacteristics.TrackedDevice |
            handCharacteristic;

        if ((device.characteristics & required) != required || IsLikelyBodyTracker(device))
        {
            return false;
        }

        if ((device.characteristics & InputDeviceCharacteristics.HeldInHand) != 0)
        {
            return true;
        }

        string descriptor = DeviceDescriptor(device);
        return descriptor.Contains("controller") ||
            descriptor.Contains("touch") ||
            descriptor.Contains("quest") ||
            descriptor.Contains("oculus") ||
            descriptor.Contains("meta") ||
            descriptor.Contains("index") ||
            descriptor.Contains("knuckles") ||
            descriptor.Contains("vive wand") ||
            descriptor.Contains("cosmos");
    }

    public static string Describe(InputDevice device)
    {
        return $"'{device.name}' ({device.manufacturer}, {device.characteristics})";
    }

    private static string DeviceDescriptor(InputDevice device)
    {
        return $"{device.name} {device.manufacturer}".ToLowerInvariant();
    }
}
