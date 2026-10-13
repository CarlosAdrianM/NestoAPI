using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Picking
{
    /// <summary>Sugerencia 575: un cambio de ruta que pide almacén desde el aviso del picking.</summary>
    public class CambioRutaPedidoRequest
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>La ruta que almacén vio en el aviso: si el pedido ya no la lleva, no se toca (alguien la ha cambiado).</summary>
        public string RutaAnterior { get; set; }
        public string RutaNueva { get; set; }
    }

    public class CambiarRutaPedidosRequest
    {
        public List<CambioRutaPedidoRequest> Cambios { get; set; } = new List<CambioRutaPedidoRequest>();
        /// <summary>Solo si no hay Identity (tests): manda siempre el usuario del JWT.</summary>
        public string Usuario { get; set; }
    }

    public class ResultadoCambioRutaDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string RutaNueva { get; set; }
        public bool Cambiado { get; set; }
        /// <summary>Por qué no se ha cambiado, en lenguaje de usuario. Null si se ha cambiado.</summary>
        public string Error { get; set; }
    }

    /// <summary>Sugerencia 575: el acceso a datos (sustituible en los tests).</summary>
    public interface IRepositorioSugerenciasRuta
    {
        /// <summary>
        /// Los pedidos que entrarían en el picking (líneas pendientes de Algete sin picking con fecha de entrega hasta
        /// <paramref name="fechaPicking"/>) y que van por OnTime. Con <paramref name="pedido"/>, solo ese; con
        /// <paramref name="cliente"/>, solo los de ese cliente; sin nada, los del picking de rutas.
        /// </summary>
        Task<List<DatosPedidoSugerenciaRuta>> LeerCandidatos(string empresa, int? pedido, string cliente, DateTime fechaPicking);

        /// <summary>
        /// Cambia la ruta si el pedido sigue con <paramref name="rutaAnterior"/> y deja rastro de quién y cuándo en
        /// LogModificaciones. Devuelve las filas cambiadas (0 si la ruta ya no era la que se vio).
        /// </summary>
        Task<int> CambiarRuta(string empresa, int pedido, string rutaAnterior, string rutaNueva, string usuario, string formulario);
    }

    /// <summary>
    /// Sugerencia 575 (Alfredo, almacén): antes de sacar el picking, los pedidos que van por OnTime y que seguramente
    /// saldrán por nuestra ruta (AT/16), con la ruta propuesta y el motivo, para que almacén les cambie la ruta ANTES del
    /// picking (y así NestoAPI#588 mire el día de entrega de nuestra ruta). Decisión de Carlos: solo se avisa a almacén al
    /// sacar el picking; a quien mete el pedido no se le dice nada, y nunca se cambia sola.
    /// </summary>
    public class GestorSugerenciasRuta
    {
        /// <summary>Lo que queda en LogModificaciones.Formulario para saber de dónde vino el cambio.</summary>
        public const string FORMULARIO = "Picking: sugerencia de ruta propia (575)";

        private readonly IRepositorioSugerenciasRuta repositorio;

        public GestorSugerenciasRuta(IRepositorioSugerenciasRuta repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <summary>Lo que se deja en ELMAH si falla un cambio (sustituible en los tests).</summary>
        internal Action<Exception> RegistrarEnElmah { get; set; } = ElmahHelper.Log;

        public async Task<List<SugerenciaRutaPedidoDTO>> LeerSugerencias(string empresa, int? pedido, string cliente, DateTime fechaPicking)
        {
            int? pedidoFiltro = pedido.HasValue && pedido.Value > 0 ? pedido : null;
            string clienteFiltro = pedidoFiltro == null && !string.IsNullOrWhiteSpace(cliente) ? cliente.Trim() : null;
            string empresaFiltro = pedidoFiltro != null
                ? (string.IsNullOrWhiteSpace(empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim())
                : null;

            List<DatosPedidoSugerenciaRuta> candidatos = await repositorio
                .LeerCandidatos(empresaFiltro, pedidoFiltro, clienteFiltro, fechaPicking.Date).ConfigureAwait(false)
                ?? new List<DatosPedidoSugerenciaRuta>();

            return candidatos
                .Select(ReglasRutaPropia.Sugerir)
                .Where(s => s != null)
                .OrderBy(s => s.Empresa)
                .ThenBy(s => s.Pedido)
                .ToList();
        }

        public async Task<List<ResultadoCambioRutaDTO>> CambiarRutas(IEnumerable<CambioRutaPedidoRequest> cambios, string usuario)
        {
            var resultados = new List<ResultadoCambioRutaDTO>();
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            foreach (CambioRutaPedidoRequest cambio in cambios ?? Enumerable.Empty<CambioRutaPedidoRequest>())
            {
                if (cambio == null)
                {
                    continue;
                }
                string empresa = string.IsNullOrWhiteSpace(cambio.Empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : cambio.Empresa.Trim();
                string rutaNueva = cambio.RutaNueva?.Trim().ToUpperInvariant();
                string rutaAnterior = cambio.RutaAnterior?.Trim().ToUpperInvariant();
                var resultado = new ResultadoCambioRutaDTO { Empresa = empresa, Pedido = cambio.Pedido, RutaNueva = rutaNueva };
                resultados.Add(resultado);

                if (cambio.Pedido <= 0)
                {
                    resultado.Error = "Falta el número de pedido.";
                    continue;
                }
                if (!ReglasRutaPropia.EsRutaPropia(rutaNueva))
                {
                    resultado.Error = $"Desde aquí solo se puede pasar a nuestra ruta ({string.Join(" o ", ReglasRutaPropia.RUTAS_PROPIAS)}), no a «{cambio.RutaNueva?.Trim()}».";
                    continue;
                }
                if (string.IsNullOrWhiteSpace(rutaAnterior))
                {
                    resultado.Error = "Falta la ruta que llevaba el pedido.";
                    continue;
                }
                if (rutaAnterior == rutaNueva)
                {
                    resultado.Cambiado = true;
                    continue;
                }

                try
                {
                    int filas = await repositorio.CambiarRuta(empresa, cambio.Pedido, rutaAnterior, rutaNueva, usuarioAuditoria, FORMULARIO).ConfigureAwait(false);
                    if (filas > 0)
                    {
                        resultado.Cambiado = true;
                    }
                    else
                    {
                        resultado.Error = $"El pedido {cambio.Pedido} ya no va por la {rutaAnterior} (o no existe): no se ha tocado.";
                    }
                }
                catch (Exception ex)
                {
                    RegistrarEnElmah?.Invoke(new Exception($"Sugerencia 575: no se ha podido cambiar la ruta del pedido {cambio.Pedido} ({empresa}) de {rutaAnterior} a {rutaNueva}", ex));
                    resultado.Error = $"No se ha podido cambiar la ruta del pedido {cambio.Pedido}: {ex.GetBaseException().Message}";
                }
            }

            return resultados;
        }
    }
}
