using NestoAPI.Models.Clientes;
using ModeloContactoEntrada = ModeloLlamadaPedido.Features.ModeloContactoEntrada;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#603 c3b: un rapport T/V/W con Estado 0 (se habló).</summary>
    public class RapportContactoCrudo
    {
        public RapportContactoCrudo(DateTime fecha, bool pedido)
        {
            Fecha = fecha;
            Pedido = pedido;
        }
        public DateTime Fecha { get; }
        /// <summary>El rapport se marcó con pedido.</summary>
        public bool Pedido { get; }
    }

    /// <summary>NestoAPI#603 c3b: un pedido = un día distinto (CabPedidoVta.Fecha) con líneas TipoLinea 1, base &gt; 0, NotaEntrega 0, Estado ≥ -1.</summary>
    public class PedidoDiaContacto
    {
        public PedidoDiaContacto(DateTime dia, decimal importe)
        {
            Dia = dia.Date;
            Importe = importe;
        }
        public DateTime Dia { get; }
        public decimal Importe { get; }
    }

    /// <summary>NestoAPI#603 c3b: cantidad vendida por mes (AAAAMM) y grupo+subgrupo (sin MMP).</summary>
    public class VentaGrupoMesContacto
    {
        public VentaGrupoMesContacto(int annoMes, string grupoSubgrupo, decimal cantidad)
        {
            AnnoMes = annoMes;
            GrupoSubgrupo = grupoSubgrupo;
            Cantidad = cantidad;
        }
        public int AnnoMes { get; }
        public string GrupoSubgrupo { get; }
        public decimal Cantidad { get; }
    }

    /// <summary>
    /// NestoAPI#603 c3b: lo que se sabe de un cliente/contacto, ordenado por fecha (copia de HistorialCliente del repo de
    /// entrenamiento ModeloLlamadaPedido). <see cref="UltimaInteraccion"/> no es del modelo: es el filtro de 7 días del
    /// endpoint antiguo (GetClientesProbabilidadVenta).
    /// </summary>
    public class HistorialContacto
    {
        private bool[] positivos;

        public HistorialContacto(string clienteId, IEnumerable<PedidoDiaContacto> pedidos, IEnumerable<RapportContactoCrudo> rapports,
            IEnumerable<VentaGrupoMesContacto> ventas, DateTime? ultimaInteraccion = null)
        {
            ClienteId = clienteId;
            // Un pedido = un día: se agrupan por si vienen varias filas del mismo día.
            Pedidos = (pedidos ?? Enumerable.Empty<PedidoDiaContacto>())
                .GroupBy(p => p.Dia.Date)
                .Select(g => new PedidoDiaContacto(g.Key, g.Sum(p => p.Importe)))
                .OrderBy(p => p.Dia).ToList();
            Rapports = (rapports ?? Enumerable.Empty<RapportContactoCrudo>()).OrderBy(r => r.Fecha).ToList();
            Ventas = (ventas ?? Enumerable.Empty<VentaGrupoMesContacto>()).ToList();
            UltimaInteraccion = ultimaInteraccion;
        }

        public string ClienteId { get; }
        public IReadOnlyList<PedidoDiaContacto> Pedidos { get; }
        public IReadOnlyList<RapportContactoCrudo> Rapports { get; }
        public IReadOnlyList<VentaGrupoMesContacto> Ventas { get; }
        /// <summary>Último rapport Estado 0 sin pedido, de cualquier tipo (definición del endpoint antiguo). Null si no hay.</summary>
        public DateTime? UltimaInteraccion { get; }

        /// <summary>Etiqueta de cada rapport (mismo índice que <see cref="Rapports"/>), calculada una vez.</summary>
        public bool EsPositivo(int indiceRapport)
        {
            if (positivos == null)
            {
                positivos = Rapports.Select(r => CalculadoraFeaturesContacto.EsPositivo(r, this)).ToArray();
            }
            return positivos[indiceRapport];
        }
    }

    /// <summary>
    /// NestoAPI#603 c3b: cálculo de las features del modelo de contactos SIN FUGA (solo datos anteriores al día del
    /// contacto). Es copia línea a línea de ModeloLlamadaPedido\Features\CalculadoraFeatures.cs (la definición con la que
    /// se entrena): si cambia allí, hay que cambiarlo aquí y reentrenar. En la API, el día del contacto es hoy.
    /// </summary>
    public static class CalculadoraFeaturesContacto
    {
        public const float SIN_PEDIDO_DIAS = 1000f;
        public const int VENTANA_POSITIVO_DIAS = 7;
        public const int MESES_HISTORIAL = 24;
        public const float PRIOR_CONVERSION = 0.25f;
        public const float PESO_PRIOR = 2f;
        public const int HORA_TARDE = 15;
        public const string SIN_GRUPO = "NADA";

        public static int DiaSemanaIso(DateTime fecha) => ((int)fecha.DayOfWeek + 6) % 7 + 1;

        public static int AnnoMes(DateTime fecha) => fecha.Year * 100 + fecha.Month;

        /// <summary>Positivo = rapport marcado con pedido o pedido del cliente en [día, día + 7].</summary>
        public static bool EsPositivo(RapportContactoCrudo rapport, HistorialContacto historial)
        {
            if (rapport.Pedido)
            {
                return true;
            }
            DateTime dia = rapport.Fecha.Date;
            DateTime limite = dia.AddDays(VENTANA_POSITIVO_DIAS);
            return historial.Pedidos.Any(p => p.Dia >= dia && p.Dia <= limite);
        }

        /// <summary>Features de un contacto en <paramref name="fechaContacto"/> (fecha y hora) del tipo indicado ("Llamada"...).</summary>
        public static ModeloContactoEntrada Calcular(HistorialContacto h, DateTime fechaContacto, string tipoInteraccion)
        {
            DateTime d = fechaContacto.Date;
            DateTime hace6 = d.AddMonths(-6), hace11 = d.AddMonths(-11), hace12 = d.AddMonths(-12), hace24 = d.AddMonths(-MESES_HISTORIAL);

            int pedidos12 = 0, pedidos24 = 0, mismoMes = 0;
            decimal importe12 = 0, importeA = 0, importeB = 0;
            DateTime? ultimo = null;
            foreach (PedidoDiaContacto p in h.Pedidos)
            {
                if (p.Dia >= d) break; // ordenados: nada del día del contacto en adelante
                if (p.Dia < hace24) continue;
                pedidos24++;
                ultimo = p.Dia;
                if (p.Dia >= hace12)
                {
                    pedidos12++;
                    importe12 += p.Importe;
                    if (p.Dia >= hace6) importeA += p.Importe; else importeB += p.Importe;
                    if (p.Dia < hace11) mismoMes++;
                }
            }

            ConversionPrevia(h, d, out int contactos, out int positivosPrevios);

            return new ModeloContactoEntrada
            {
                ClienteId = h.ClienteId,
                TipoInteraccion = tipoInteraccion,
                Mes = d.Month.ToString(),
                DiaSemana = DiaSemanaIso(d).ToString(),
                GrupoSubgrupoMasVendido = GrupoSubgrupoMasVendido(h, d),
                EsPorLaTarde = fechaContacto.Hour >= HORA_TARDE ? 1f : 0f,
                Pedidos12Meses = pedidos12,
                Importe12Meses = (float)importe12,
                DiasDesdeUltimoPedido = ultimo.HasValue ? (float)(d - ultimo.Value).TotalDays : SIN_PEDIDO_DIAS,
                DiasEntrePedidos = pedidos12 > 0 ? 365f / pedidos12 : SIN_PEDIDO_DIAS,
                PedidosMismoMesAnnoAnterior = mismoMes,
                ImporteMedioPedido = pedidos12 > 0 ? (float)(importe12 / pedidos12) : 0f,
                TendenciaImporte = importeA + importeB > 0 ? (float)((importeA - importeB) / (importeA + importeB)) : 0f,
                TasaConversionCliente = (positivosPrevios + PESO_PRIOR * PRIOR_CONVERSION) / (contactos + PESO_PRIOR),
                ContactosPrevios = contactos,
                SinHistorial = pedidos24 == 0 ? 1f : 0f
            };
        }

        /// <summary>Contactos previos (24 meses) cuya ventana de 7 días está cerrada antes de d, y cuántos fueron positivos.</summary>
        public static void ConversionPrevia(HistorialContacto h, DateTime d, out int contactos, out int positivos)
        {
            DateTime hace24 = d.AddMonths(-MESES_HISTORIAL);
            contactos = 0;
            positivos = 0;
            for (int i = 0; i < h.Rapports.Count; i++)
            {
                DateTime dia = h.Rapports[i].Fecha.Date;
                if (dia.AddDays(VENTANA_POSITIVO_DIAS) >= d) break; // ordenados por fecha
                if (dia < hace24) continue;
                contactos++;
                if (h.EsPositivo(i)) positivos++;
            }
        }

        /// <summary>Grupo+subgrupo con más cantidad en los 12 meses naturales anteriores al mes de d (sin incluirlo); empate: orden alfabético.</summary>
        public static string GrupoSubgrupoMasVendido(HistorialContacto h, DateTime d)
        {
            DateTime inicioMes = new DateTime(d.Year, d.Month, 1);
            int desde = AnnoMes(inicioMes.AddMonths(-12));
            int hasta = AnnoMes(inicioMes); // excluido
            return h.Ventas
                .Where(v => v.AnnoMes >= desde && v.AnnoMes < hasta && !string.IsNullOrWhiteSpace(v.GrupoSubgrupo))
                .GroupBy(v => v.GrupoSubgrupo)
                .Select(g => new { Grupo = g.Key, Cantidad = g.Sum(v => v.Cantidad) })
                .Where(g => g.Cantidad > 0)
                .OrderByDescending(g => g.Cantidad).ThenBy(g => g.Grupo, StringComparer.Ordinal)
                .Select(g => g.Grupo)
                .FirstOrDefault() ?? SIN_GRUPO;
        }
    }
}
