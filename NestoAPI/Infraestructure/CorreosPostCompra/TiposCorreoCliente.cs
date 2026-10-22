using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>NestoAPI#532: un tipo de correo automático a clientes del que se puede dar de baja por separado.</summary>
    public class TipoCorreoCliente
    {
        public TipoCorreoCliente(string tipo, string nombre, string descripcion)
        {
            Tipo = tipo;
            Nombre = nombre;
            Descripcion = descripcion;
        }

        /// <summary>Lo que se guarda en CorreosPostCompraBajas.Tipo y viaja en la URL del PUT de la app.</summary>
        public string Tipo { get; }
        public string Nombre { get; }
        public string Descripcion { get; }
    }

    /// <summary>
    /// NestoAPI#532 (bajas por tipo): los correos automáticos a clientes que no son de servicio (los de pedidos, facturas,
    /// envíos o cobros no se pueden quitar: son la operativa). Una baja con Tipo NULL en CorreosPostCompraBajas los quita
    /// todos; con Tipo, solo ese.
    /// </summary>
    public static class TiposCorreoCliente
    {
        /// <summary>El recordatorio de reposición de los jueves (RecordatorioReposicionJobsService).</summary>
        public const string RECORDATORIO_REPOSICION = "RecordatorioReposicion";
        /// <summary>El correo posventa de los sábados con los vídeos de lo comprado esa semana (CorreosPostCompraJobsService, #74).</summary>
        public const string VIDEOS_COMPRAS = "VideosCompras";

        public static readonly IReadOnlyList<TipoCorreoCliente> Todos = new List<TipoCorreoCliente>
        {
            new TipoCorreoCliente(RECORDATORIO_REPOSICION, "Aviso de reposición",
                "Cuando, por cómo suele comprarlo, puede estar a punto de quedarse sin un producto, le avisamos por si le viene bien " +
                "tenerlo a mano. Como mucho, un correo cada dos semanas."),
            new TipoCorreoCliente(VIDEOS_COMPRAS, "Vídeos de sus compras",
                "Los sábados le enviamos los vídeos y protocolos de los productos que ha comprado esa semana, con ideas para sacarles partido.")
        };

        /// <summary>El tipo con ese código, sin distinguir mayúsculas; null si no existe.</summary>
        public static TipoCorreoCliente Buscar(string tipo)
            => string.IsNullOrWhiteSpace(tipo)
                ? null
                : Todos.FirstOrDefault(t => string.Equals(t.Tipo, tipo.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
