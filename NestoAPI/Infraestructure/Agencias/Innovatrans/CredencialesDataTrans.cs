namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Bloque de autenticación de DataTrans DTX (va en cada operación SOAP). La <see cref="Clave"/>
    /// es el MD5 de la contraseña (la contraseña en claro nunca se guarda aquí ni viaja).
    /// </summary>
    public class CredencialesDataTrans
    {
        public string Identificador { get; set; }
        public string Empresa { get; set; }
        public string Email { get; set; }
        public string Clave { get; set; }
    }
}
