namespace NestoAPI.Models
{
    public class Parametros
    {
        public struct Claves
        {
            // Colocar por orden alfabético
            public const string CorreoDefecto = "CorreoDefecto";
            public const string CuentaBancoTarjeta = "CuentaBancoTarjeta";
            /// <summary>NestoAPI#345: días de antelación con los que se giran los recibos
            /// (default 1 si el usuario no tiene el parámetro; p. ej. 5 = estilo eléctricas).</summary>
            public const string DiasAntelacionRemesa = "DiasAntelacionRemesa";
        }
    }
}
