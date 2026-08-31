namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// NestoAPI#429 (punto 1): el núcleo de la validación de API key, en UN solo sitio.
    ///
    /// Había dos copias divergentes del mismo control: la de
    /// <c>AuthController.PrestashopLogin</c> (setting <c>ApiKeyPrestashop</c>, cabecera
    /// <c>X-API-KEY</c>) y la de <c>NotificacionesController.NotificarNuevoProtocolo</c> (setting
    /// <c>NotificacionesApiKey</c>, cabecera <c>Authorization</c>). La segunda fallaba en cerrado;
    /// la primera NO:
    ///
    /// <code>
    ///     private readonly string _apiKeyPrestashop = ConfigurationManager.AppSettings["ApiKeyPrestashop"];
    ///     if (apiKey != _apiKeyPrestashop) { return Unauthorized(); }
    /// </code>
    ///
    /// Si la setting no estaba definida, <c>_apiKeyPrestashop</c> valía null; una petición SIN
    /// cabecera dejaba <c>apiKey</c> a null; null != null es falso, y la validación se superaba.
    /// El endpoint quedaba abierto — y es un endpoint que emite JWT de cliente SALTÁNDOSE el
    /// código por correo. Como <c>appSettings</c> está externalizado a <c>secretos.config</c>, que
    /// no está en control de versiones, bastaba un despliegue con ese fichero mal copiado.
    /// </summary>
    internal static class ValidadorApiKey
    {
        /// <summary>
        /// ¿Es válida la clave recibida? Falla en CERRADO: si la clave esperada no está
        /// configurada, no la valida nada — se rechaza a todo el mundo.
        ///
        /// Un endpoint que deja de funcionar porque falta un secreto se nota en cinco minutos y se
        /// arregla; un endpoint que se queda abierto porque falta un secreto no se nota nunca.
        /// </summary>
        internal static bool EsValida(string claveEsperada, string claveRecibida)
        {
            if (string.IsNullOrWhiteSpace(claveEsperada))
            {
                return false;
            }

            // Tiempo constante: sobre un endpoint anónimo, el == de toda la vida deja adivinar la
            // clave carácter a carácter midiendo lo que tarda en contestar.
            return ComparacionSegura.SonIguales(claveEsperada, claveRecibida);
        }
    }
}
