using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso
{
    /// <summary>
    /// NestoAPI#734 (Carlos, 11/11/26): la pregunta a Administración es una notificación URGENTE (#775): ventana roja con pitido en Nesto.
    /// Referencia «empresa/pedido» (una sola consulta abierta por pedido); sigue pendiente mientras la consulta esté abierta; al contestar,
    /// «notificacionResuelta» cierra la ventana en todos los Nesto.
    /// </summary>
    public static class UrgenteConsultaReembolso
    {
        /// <summary>«1/929559».</summary>
        public static string Referencia(string empresa, int pedido)
            => $"{(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim())}/{pedido.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>«1/929559» → ("1", 929559); false si no tiene esa forma.</summary>
        public static bool LeerReferencia(string referencia, out string empresa, out int pedido)
        {
            empresa = null;
            pedido = 0;
            string[] partes = (referencia ?? string.Empty).Split('/');
            if (partes.Length != 2 || string.IsNullOrWhiteSpace(partes[0])
                || !int.TryParse(partes[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pedido) || pedido <= 0)
            {
                return false;
            }
            empresa = partes[0].Trim();
            return true;
        }

        /// <summary>El «notificacionResuelta» de una consulta contestada. Resultado: «ConReembolso» o «SinReembolso».</summary>
        public static NotificacionResueltaDTO Resuelta(ConsultaReembolsoDTO consulta) => new NotificacionResueltaDTO
        {
            Tipo = TextosConsultaReembolso.TIPO_NOTIFICACION,
            Referencia = Referencia(consulta.Empresa, consulta.Pedido),
            Respondio = Retencion.TextosRetencion.NombreUsuario(consulta.UsuarioRespuesta),
            Resultado = consulta.ConReembolso == true ? "ConReembolso" : "SinReembolso"
        };
    }

    /// <summary>NestoAPI#734: GET api/Notificaciones/Urgentes: las consultas de reembolso que siguen abiertas.</summary>
    public class ComprobadorConsultasReembolsoAbiertas : IComprobadorUrgentesPendientes
    {
        private readonly IRepositorioConsultasReembolso repositorio;

        public ComprobadorConsultasReembolsoAbiertas(IRepositorioConsultasReembolso repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public string Tipo => TextosConsultaReembolso.TIPO_NOTIFICACION;

        public async Task<ISet<string>> Pendientes(IReadOnlyCollection<string> referencias)
        {
            var pendientes = new HashSet<string>(StringComparer.Ordinal);
            if (referencias == null || referencias.Count == 0 || !repositorio.TablaExiste())
            {
                return pendientes;
            }
            foreach (string referencia in referencias.Distinct())
            {
                if (UrgenteConsultaReembolso.LeerReferencia(referencia, out string empresa, out int pedido)
                    && await repositorio.LeerAbierta(empresa, pedido).ConfigureAwait(false) != null)
                {
                    _ = pendientes.Add(referencia);
                }
            }
            return pendientes;
        }
    }
}
