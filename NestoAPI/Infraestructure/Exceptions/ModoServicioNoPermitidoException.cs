using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// NestoAPI#518: el modo de servicio que llega al guardar ya no tiene sentido para el pedido (normalmente
    /// porque el stock ha cambiado mientras se montaba). Decisión de Carlos (23/09/26): NO se corrige en
    /// silencio; se rechaza con un mensaje accionable y el modo que sí vale, para que el cliente lo preseleccione.
    /// JSON (GlobalExceptionFilter): error.code = "MODO_SERVICIO_NO_PERMITIDO", error.message, y en
    /// error.details: modoSugerido (byte), modoSugeridoNombre, modosPermitidos (lista de bytes).
    /// </summary>
    public class ModoServicioNoPermitidoException : NestoBusinessException
    {
        public const string CODIGO = "MODO_SERVICIO_NO_PERMITIDO";

        public byte ModoSugerido { get; }

        public ModoServicioNoPermitidoException(string mensaje, byte modoSugerido, string modoSugeridoNombre,
            IEnumerable<byte> modosPermitidos, string empresa = null, int? pedido = null)
            : base(mensaje, new ErrorContext { ErrorCode = CODIGO, Empresa = empresa, Pedido = pedido }
                .WithData("modoSugerido", modoSugerido)
                .WithData("modoSugeridoNombre", modoSugeridoNombre)
                .WithData("modosPermitidos", new List<byte>(modosPermitidos ?? new List<byte>())))
        {
            ModoSugerido = modoSugerido;
        }
    }
}
