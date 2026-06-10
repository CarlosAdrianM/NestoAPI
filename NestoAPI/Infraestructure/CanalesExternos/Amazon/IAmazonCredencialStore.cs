using System;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: persistencia de la credencial Amazon en dbo.AmazonSpApiCredencial.
    /// Único punto de acceso a la tabla (SQL crudo); el resto del código pasa por aquí.
    /// </summary>
    public interface IAmazonCredencialStore
    {
        AmazonSpApiCredencial Obtener();

        /// <summary>Persiste el secreto nuevo recibido por SQS (y sus caducidades).</summary>
        void GuardarSecretoNuevo(string clientId, string nuevoSecret, DateTime? secretExpiry, DateTime? oldSecretExpiry, string usuario);

        /// <summary>Marca que se ha solicitado una rotación (guard anti doble-rotación durante la gracia de 7 días).</summary>
        void MarcarRotacionSolicitada(string clientId);
    }
}
