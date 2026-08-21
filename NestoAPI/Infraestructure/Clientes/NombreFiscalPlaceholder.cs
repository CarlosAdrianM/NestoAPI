namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#388 (21/08/26): cuando el NIF es de una persona JURÍDICA, los clientes (wizard
    /// de Nesto y de NestoApp) NO dejan escribir el nombre: mandan un relleno ("UNDEFINED" en
    /// Nesto, "undefined" en NestoApp) y adoptan como nombre del cliente la razón social que
    /// devuelve el censo de la AEAT.
    ///
    /// Con el certificado caducado no hay censo al que preguntar, así que ese relleno NO puede
    /// devolverse tal cual (se acabaría dando de alta un cliente llamado "UNDEFINED") ni puede
    /// llegar a grabarse en la ficha. Este es el único sitio donde se conoce el convenio.
    /// </summary>
    public static class NombreFiscalPlaceholder
    {
        /// <summary>Relleno que mandan los clientes cuando esperan el nombre del censo.</summary>
        public const string RELLENO = "UNDEFINED";

        /// <summary>
        /// true si <paramref name="nombre"/> es el relleno de "que me lo diga Hacienda"
        /// (comparación indiferente a mayúsculas: Nesto manda "UNDEFINED", NestoApp "undefined").
        /// </summary>
        public static bool EsRelleno(string nombre)
        {
            return !string.IsNullOrWhiteSpace(nombre)
                && string.Equals(nombre.Trim(), RELLENO, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// true si el nombre no sirve como nombre fiscal para grabar en la ficha: en blanco o
        /// el relleno que debía haber rellenado el censo.
        /// </summary>
        public static bool NoEsNombreFiscalValido(string nombre)
        {
            return string.IsNullOrWhiteSpace(nombre) || EsRelleno(nombre);
        }

        /// <summary>Mensaje único para el usuario cuando se intenta grabar el relleno.</summary>
        public const string MENSAJE_ERROR =
            "No se ha podido obtener el nombre fiscal del censo de la AEAT (certificado caducado). " +
            "Escriba usted el nombre fiscal del cliente antes de continuar.";
    }
}
