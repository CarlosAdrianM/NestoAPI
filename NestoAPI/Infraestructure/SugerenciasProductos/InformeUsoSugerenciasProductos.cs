using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>
    /// NestoAPI#669: una sugerencia enseñada (o una sesión sin ninguna) para el informe de uso. Lo que lee
    /// <see cref="InformeUsoSugerenciasProductos.SQL_USO"/>.
    /// </summary>
    public class FilaUsoSugerencia
    {
        public Guid SesionId { get; set; }
        public string Vendedor { get; set; }
        public string Aplicacion { get; set; }
        public int? Pedido { get; set; }
        /// <summary>Null: la sesión no enseñó ninguna sugerencia.</summary>
        public int? IdMostrada { get; set; }
        public string Respuesta { get; set; }
        /// <summary>Solo el principio del Texto (lo justo para ver las marcas provisionales de «Quitar» y «Sustituida»).</summary>
        public string Texto { get; set; }
        public bool? EntroEnPedido { get; set; }
    }

    /// <summary>NestoAPI#669: el uso de las sugerencias de un vendedor en una aplicación (o el total).</summary>
    public class UsoSugerenciasGrupoDTO
    {
        public string Vendedor { get; set; }
        public string Aplicacion { get; set; }
        public int Sesiones { get; set; }
        public int SesionesConPedido { get; set; }
        public int Mostradas { get; set; }
        /// <summary>Las enseñadas en sesiones que acabaron en pedido (solo en esas puede haber acierto sin ofrecer).</summary>
        public int MostradasConPedido { get; set; }
        /// <summary>✓ «Anadido»: ofrecida y aceptada.</summary>
        public int Anadidas { get; set; }
        /// <summary>✗ «NoLeInteresa»: ofrecida y rechazada por el cliente.</summary>
        public int NoLeInteresa { get; set; }
        /// <summary>🚫 «DescartadoVendedor» (también la provisional de antes del script Issue670b): el vendedor no la ofrece.</summary>
        public int Quitadas { get; set; }
        /// <summary>NestoAPI#682: la API la cambió por otra mejor sin que nadie la tocara (no es respuesta de nadie).</summary>
        public int Sustituidas { get; set; }
        /// <summary>Sin ✓, ✗ ni 🚫 y el producto acabó en el pedido: la predicción acertó, pero NO consta que se ofreciera.</summary>
        public int AciertoSinOfrecer { get; set; }
        /// <summary><see cref="Anadidas"/> + <see cref="AciertoSinOfrecer"/>.</summary>
        public int Aciertos { get; set; }
        /// <summary>Aciertos / Mostradas, en %, con un decimal. Null sin mostradas.</summary>
        public decimal? PorcentajeAcierto { get; set; }
        /// <summary>Aciertos de las sesiones con pedido / MostradasConPedido, en %, con un decimal. Null sin mostradas en sesiones con pedido.</summary>
        public decimal? PorcentajeAciertoConPedido { get; set; }
    }

    public class UsoSugerenciasDTO
    {
        public DateTime Desde { get; set; }
        /// <summary>Exclusivo: sesiones empezadas antes de esta fecha.</summary>
        public DateTime Hasta { get; set; }
        public List<UsoSugerenciasGrupoDTO> PorVendedor { get; set; } = new List<UsoSugerenciasGrupoDTO>();
        public UsoSugerenciasGrupoDTO Total { get; set; } = new UsoSugerenciasGrupoDTO();
    }

    /// <summary>
    /// NestoAPI#669: informe de uso de las sugerencias de productos por vendedor y aplicación (GET api/SugerenciasProductos/Uso).
    /// <para><b>«Acierto sin ofrecer»</b> (decisión de Carlos, 21/10/26): casi nadie pulsa ✓/✗, pero a veces el producto sugerido
    /// acaba en el pedido sin pulsar nada. Eso NO significa que se ofreciera: significa que la predicción acertó. Se CALCULA aquí,
    /// sin escribir nada, con lo que ya se guarda al grabar el pedido: <c>EntroEnPedido</c> de SugerenciasProductosMostradas (lo
    /// marca <see cref="ServicioSugerenciasProductos.EnlazarPedido"/> en POST, PUT y UnirPedidos, con la sesión del pedido) y
    /// Respuesta sin contestar (NULL o «Sustituida», que no es respuesta de nadie). Nunca se guarda como «Anadido».</para>
    /// <para>Por qué calcularlo y no una columna o tabla nueva: el dato ya está (EntroEnPedido), así que no hace falta DDL ni
    /// escribir nada más dentro del guardado del pedido (que en UnirPedidos va en un TransactionScope, NestoAPI#687); y al no
    /// existir como respuesta, ni las exclusiones de 60 días (SQL_RECHAZADOS: solo «NoLeInteresa» y «DescartadoVendedor») ni el
    /// futuro modelo 2 de aceptación pueden tomarlo por un «sí» del cliente. El modelo 1 se entrena con el histórico de
    /// pedidos (SugerenciasProductos.Nucleo no lee las tablas de sugerencias): esto solo sirve para EVALUARLO.</para>
    /// </summary>
    public static class InformeUsoSugerenciasProductos
    {
        public const string SIN_VENDEDOR = "(sin vendedor)";
        public const int DIAS_POR_DEFECTO = 30;

        // Sesiones por fecha de inicio; LEFT JOIN para contar también las que no enseñaron nada. Pocas filas (cientos al mes).
        internal const string SQL_USO = @"
SELECT s.Id AS SesionId, RTRIM(s.Vendedor) AS Vendedor, s.Aplicacion, s.Pedido,
       m.Id AS IdMostrada, m.Respuesta, LEFT(m.Texto, 30) AS Texto, m.EntroEnPedido
FROM dbo.SugerenciasProductosSesiones s WITH (NOLOCK)
LEFT JOIN dbo.SugerenciasProductosMostradas m WITH (NOLOCK) ON m.SesionId = s.Id
WHERE s.FechaInicio >= @p0 AND s.FechaInicio < @p1";

        /// <summary>Sin las tablas del script Issue670, lista vacía.</summary>
        public static async Task<List<FilaUsoSugerencia>> Leer(NVEntities db, DateTime desde, DateTime hasta)
        {
            bool existen = await db.Database.SqlQuery<bool>(SugerenciasProductosSql.SQL_EXISTEN_TABLAS).FirstAsync().ConfigureAwait(false);
            if (!existen)
            {
                return new List<FilaUsoSugerencia>();
            }
            return await db.Database.SqlQuery<FilaUsoSugerencia>(SQL_USO,
                SugerenciasProductosSql.Fecha("@p0", desde), SugerenciasProductosSql.Fecha("@p1", hasta)).ToListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Sin ✓, ✗ ni 🚫 (Respuesta NULL o «Sustituida», también la provisional) y el producto en el pedido guardado.
        /// Una «NoLeInteresa» o una «🚫 Quitar» que acaba en el pedido no cuenta (alguien dijo que no); una «Anadido» cuenta
        /// como añadida, no aquí.
        /// </summary>
        public static bool EsAciertoSinOfrecer(string respuesta, bool? entroEnPedido)
            => entroEnPedido == true && (respuesta == null || respuesta == RespuestasSugerencia.SUSTITUIDA);

        public static UsoSugerenciasDTO Calcular(IEnumerable<FilaUsoSugerencia> filas, DateTime desde, DateTime hasta)
        {
            List<FilaUsoSugerencia> lista = (filas ?? Enumerable.Empty<FilaUsoSugerencia>()).Where(f => f != null).ToList();
            var resultado = new UsoSugerenciasDTO
            {
                Desde = desde,
                Hasta = hasta,
                PorVendedor = lista
                    .GroupBy(f => new { Vendedor = Vendedor(f.Vendedor), Aplicacion = f.Aplicacion?.Trim() ?? string.Empty })
                    .Select(g => Grupo(g, g.Key.Vendedor, g.Key.Aplicacion))
                    .OrderBy(g => g.Vendedor, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.Aplicacion, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Total = Grupo(lista, null, null)
            };
            return resultado;
        }

        private static UsoSugerenciasGrupoDTO Grupo(IEnumerable<FilaUsoSugerencia> filas, string vendedor, string aplicacion)
        {
            List<FilaUsoSugerencia> lista = filas.ToList();
            List<FilaUsoSugerencia> mostradas = lista.Where(f => f.IdMostrada.HasValue).ToList();
            var grupo = new UsoSugerenciasGrupoDTO
            {
                Vendedor = vendedor,
                Aplicacion = aplicacion,
                Sesiones = lista.Select(f => f.SesionId).Distinct().Count(),
                SesionesConPedido = lista.Where(f => f.Pedido.HasValue).Select(f => f.SesionId).Distinct().Count(),
                Mostradas = mostradas.Count,
                MostradasConPedido = mostradas.Count(f => f.Pedido.HasValue),
                Anadidas = mostradas.Count(f => f.Respuesta == RespuestasSugerencia.ANADIDO),
                NoLeInteresa = mostradas.Count(f => f.Respuesta == RespuestasSugerencia.NO_LE_INTERESA && !RespuestasSugerencia.EsQuitada(f.Respuesta, f.Texto)),
                Quitadas = mostradas.Count(f => RespuestasSugerencia.EsQuitada(f.Respuesta, f.Texto)),
                Sustituidas = mostradas.Count(f => f.Respuesta == RespuestasSugerencia.SUSTITUIDA || RespuestasSugerencia.EsSustituidaProvisional(f.Respuesta, f.Texto)),
                AciertoSinOfrecer = mostradas.Count(f => EsAciertoSinOfrecer(f.Respuesta, f.EntroEnPedido))
            };
            grupo.Aciertos = grupo.Anadidas + grupo.AciertoSinOfrecer;
            grupo.PorcentajeAcierto = Porcentaje(grupo.Aciertos, grupo.Mostradas);
            grupo.PorcentajeAciertoConPedido = Porcentaje(
                mostradas.Count(f => f.Pedido.HasValue && (f.Respuesta == RespuestasSugerencia.ANADIDO || EsAciertoSinOfrecer(f.Respuesta, f.EntroEnPedido))),
                grupo.MostradasConPedido);
            return grupo;
        }

        private static string Vendedor(string vendedor) => string.IsNullOrWhiteSpace(vendedor) ? SIN_VENDEDOR : vendedor.Trim();

        private static decimal? Porcentaje(int parte, int total)
            => total == 0 ? (decimal?)null : Math.Round(100m * parte / total, 1, MidpointRounding.AwayFromZero);
    }
}
