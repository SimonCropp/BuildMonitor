public enum AuthMethod
{
    Token,
    // OAuth authorization code in the system browser, redirected back to a loopback listener.
    Browser,
    // OAuth device flow: a code to type into a page the browser opens. No client secret and no
    // listener, so it is the default where a provider offers it.
    Device
}
