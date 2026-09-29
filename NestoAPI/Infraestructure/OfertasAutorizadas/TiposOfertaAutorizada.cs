using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.OfertasAutorizadas
{
    /// <summary>NestoAPI#233: textos comunes a las tres estrategias.</summary>
    internal static class TextosOfertaAutorizada
    {
        internal static readonly CultureInfo Cultura = new CultureInfo("es-ES");
        internal const string TOCA_PARA_VER_DETALLE = "Toca para ver el detalle.";
    }

    /// <summary>Pestaña «Ofertas Combinadas».</summary>
    internal class TipoOfertaCombinada : ITipoOfertaAutorizada
    {
        internal const string TIPO = "combinada";
        private readonly NVEntities db;

        public TipoOfertaCombinada(NVEntities db)
        {
            this.db = db;
        }

        public string Tipo => TIPO;

        public async Task<AvisoOfertaAutorizada> ConstruirAviso(int id)
        {
            OfertaCombinada oferta = await db.OfertasCombinadas
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);
            return oferta == null ? null : new AvisoOfertaAutorizada { Cuerpo = Cuerpo(oferta) };
        }

        internal static string Cuerpo(OfertaCombinada oferta)
        {
            string texto = oferta.Nombre?.Trim();
            if (oferta.ImporteMinimo > 0)
            {
                texto += ": desde " + oferta.ImporteMinimo.ToString("N2", TextosOfertaAutorizada.Cultura) + " €";
            }
            if (oferta.FechaHasta.HasValue)
            {
                texto += ". Válida hasta el " + oferta.FechaHasta.Value.ToString("dd/MM/yyyy", TextosOfertaAutorizada.Cultura);
            }
            return texto + ". " + TextosOfertaAutorizada.TOCA_PARA_VER_DETALLE;
        }

        public async Task AnadirVigentes(string empresa, DateTime hoy, OfertasAutorizadasDTO destino)
        {
            string empresaPadded = empresa.PadRight(3);
            List<OfertaCombinada> ofertas = await db.OfertasCombinadas
                .Include("OfertasCombinadasDetalles")
                .Include("OfertasCombinadasDetalles.Producto1")
                .Where(o => o.Empresa == empresaPadded
                    && (o.FechaDesde == null || o.FechaDesde <= hoy)
                    && (o.FechaHasta == null || o.FechaHasta >= hoy))
                .OrderBy(o => o.Id)
                .ToListAsync()
                .ConfigureAwait(false);
            destino.Combinadas = ofertas.Select(MapeadorOfertas.Combinada).ToList();
        }
    }

    /// <summary>Pestaña «Ofertas por Familia» (OfertasPermitidas generales de una familia).</summary>
    internal class TipoOfertaFamilia : ITipoOfertaAutorizada
    {
        internal const string TIPO = "familia";
        private readonly NVEntities db;

        public TipoOfertaFamilia(NVEntities db)
        {
            this.db = db;
        }

        public string Tipo => TIPO;

        public async Task<AvisoOfertaAutorizada> ConstruirAviso(int id)
        {
            OfertaPermitida oferta = await db.OfertasPermitidas
                .FirstOrDefaultAsync(o => o.NºOrden == id
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia != null)
                .ConfigureAwait(false);
            if (oferta == null)
            {
                return null;
            }

            // NestoAPI#564: una denegación prohíbe el N+M; no hay nada que ofrecer al cliente.
            if (oferta.Denegar)
            {
                return new AvisoOfertaAutorizada
                {
                    MotivoNoSeAvisa = "Es una denegación (prohíbe el N+M), no una oferta que comunicar a los vendedores"
                };
            }

            string descripcion = await db.Familias
                .Where(f => f.Empresa == oferta.Empresa && f.Número == oferta.Familia)
                .Select(f => f.Descripción)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return new AvisoOfertaAutorizada { Cuerpo = Cuerpo(oferta, descripcion) };
        }

        internal static string Cuerpo(OfertaPermitida oferta, string descripcionFamilia)
        {
            string familia = string.IsNullOrWhiteSpace(descripcionFamilia) ? oferta.Familia?.Trim() : descripcionFamilia.Trim();
            string texto = $"{familia}: lleva {oferta.CantidadConPrecio} y te regalamos {oferta.CantidadRegalo}";

            var limites = new List<string>();
            if (!string.IsNullOrWhiteSpace(oferta.FiltroProducto))
            {
                limites.Add($"productos cuyo nombre empieza por «{oferta.FiltroProducto.Trim()}»");
            }
            string subGrupo = Controllers.OfertasPermitidasFamiliaController.NormalizarSubGrupo(oferta.SubGrupo);
            if (subGrupo != null)
            {
                limites.Add($"subgrupo {subGrupo}");
            }
            if (limites.Any())
            {
                texto += " (" + string.Join(", ", limites) + ")";
            }
            return texto + ". " + TextosOfertaAutorizada.TOCA_PARA_VER_DETALLE;
        }

        public async Task AnadirVigentes(string empresa, DateTime hoy, OfertasAutorizadasDTO destino)
        {
            string empresaPadded = empresa.PadRight(3);
            List<OfertaPermitida> ofertas = await db.OfertasPermitidas
                .Where(o => o.Empresa == empresaPadded
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia != null
                    && !o.Denegar
                    && (o.FechaDesde == null || o.FechaDesde <= hoy)
                    && (o.FechaHasta == null || o.FechaHasta >= hoy))
                .OrderBy(o => o.NºOrden)
                .ToListAsync()
                .ConfigureAwait(false);

            List<string> familiaIds = ofertas.Select(o => o.Familia).Distinct().ToList();
            Dictionary<string, string> familias = await db.Familias
                .Where(f => f.Empresa == empresaPadded && familiaIds.Contains(f.Número))
                .ToDictionaryAsync(f => f.Número, f => f.Descripción)
                .ConfigureAwait(false);

            destino.Familias = ofertas.Select(o => MapeadorOfertas.Familia(o, familias)).ToList();
        }
    }

    /// <summary>Pestaña «Ofertas Escalonadas».</summary>
    internal class TipoOfertaEscalonada : ITipoOfertaAutorizada
    {
        internal const string TIPO = "escalonada";
        private readonly NVEntities db;

        public TipoOfertaEscalonada(NVEntities db)
        {
            this.db = db;
        }

        public string Tipo => TIPO;

        public async Task<AvisoOfertaAutorizada> ConstruirAviso(int id)
        {
            OfertaEscalonada oferta = await db.OfertasEscalonadas
                .Include("OfertasEscalonadasTramos")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);
            return oferta == null ? null : new AvisoOfertaAutorizada { Cuerpo = Cuerpo(oferta) };
        }

        internal static string Cuerpo(OfertaEscalonada oferta)
        {
            string texto = oferta.Nombre?.Trim() + ": descuentos por volumen";
            decimal descuentoMaximo = oferta.OfertasEscalonadasTramos?.Any() == true
                ? oferta.OfertasEscalonadasTramos.Max(t => t.Descuento)
                : 0;
            if (descuentoMaximo > 0)
            {
                texto += " de hasta el " + (descuentoMaximo * 100).ToString("0.##", TextosOfertaAutorizada.Cultura) + " %";
            }
            return texto + ". Toca para ver los tramos.";
        }

        public async Task AnadirVigentes(string empresa, DateTime hoy, OfertasAutorizadasDTO destino)
        {
            string empresaPadded = empresa.PadRight(3);
            List<OfertaEscalonada> ofertas = await db.OfertasEscalonadas
                .Include("OfertasEscalonadasProductos")
                .Include("OfertasEscalonadasProductos.Producto1")
                .Include("OfertasEscalonadasTramos")
                .Where(o => o.Empresa == empresaPadded
                    && (o.FechaDesde == null || o.FechaDesde <= hoy)
                    && (o.FechaHasta == null || o.FechaHasta >= hoy))
                .OrderBy(o => o.Id)
                .ToListAsync()
                .ConfigureAwait(false);
            destino.Escalonadas = ofertas.Select(MapeadorOfertas.Escalonada).ToList();
        }
    }
}
