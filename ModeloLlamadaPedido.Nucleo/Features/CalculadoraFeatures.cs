using ModeloLlamadaPedido.Datos;

namespace ModeloLlamadaPedido.Features;

/// <summary>
/// NestoAPI#603 c3: cálculo de las features SIN FUGA (solo datos anteriores al día del contacto). Es la definición de
/// referencia que la API tiene que replicar en inferencia (con d = hoy).
/// </summary>
public static class CalculadoraFeatures
{
    public const float SIN_PEDIDO_DIAS = 1000f;
    public const int VENTANA_POSITIVO_DIAS = 7;
    public const int MESES_HISTORIAL = 24;
    public const float PRIOR_CONVERSION = 0.25f;
    public const float PESO_PRIOR = 2f;
    public const int HORA_TARDE = 15;
    public const string SIN_GRUPO = "NADA";

    public static string TipoInteraccion(string tipo) => tipo switch
    {
        "T" => "Llamada",
        "V" => "Visita",
        "W" => "WhatsApp",
        _ => tipo
    };

    public static int DiaSemanaIso(DateTime fecha) => ((int)fecha.DayOfWeek + 6) % 7 + 1;

    /// <summary>Positivo = rapport marcado con pedido o pedido del cliente en [día, día + 7].</summary>
    public static bool EsPositivo(RapportCrudo rapport, HistorialCliente historial)
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
    public static T Calcular<T>(HistorialCliente h, DateTime fechaContacto, string tipoInteraccion) where T : ModeloContactoEntrada, new()
    {
        DateTime d = fechaContacto.Date;
        DateTime hace6 = d.AddMonths(-6), hace11 = d.AddMonths(-11), hace12 = d.AddMonths(-12), hace24 = d.AddMonths(-MESES_HISTORIAL);

        int pedidos12 = 0, pedidos24 = 0, mismoMes = 0;
        decimal importe12 = 0, importeA = 0, importeB = 0;
        DateTime? ultimo = null;
        foreach (PedidoDia p in h.Pedidos)
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

        (int contactos, int positivos) = ConversionPrevia(h, d);

        return new T
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
            TasaConversionCliente = (positivos + PESO_PRIOR * PRIOR_CONVERSION) / (contactos + PESO_PRIOR),
            ContactosPrevios = contactos,
            SinHistorial = pedidos24 == 0 ? 1f : 0f
        };
    }

    /// <summary>Contactos previos (24 meses) cuya ventana de 7 días está cerrada antes de d, y cuántos fueron positivos.</summary>
    public static (int contactos, int positivos) ConversionPrevia(HistorialCliente h, DateTime d)
    {
        DateTime hace24 = d.AddMonths(-MESES_HISTORIAL);
        int contactos = 0, positivos = 0;
        for (int i = 0; i < h.Rapports.Count; i++)
        {
            DateTime dia = h.Rapports[i].Fecha.Date;
            if (dia.AddDays(VENTANA_POSITIVO_DIAS) >= d) break; // ordenados por fecha
            if (dia < hace24) continue;
            contactos++;
            if (h.EsPositivo(i)) positivos++;
        }
        return (contactos, positivos);
    }

    /// <summary>Grupo+subgrupo con más cantidad en los 12 meses naturales anteriores al mes de d (sin incluirlo).</summary>
    public static string GrupoSubgrupoMasVendido(HistorialCliente h, DateTime d)
    {
        DateTime inicioMes = new(d.Year, d.Month, 1);
        int desde = Claves.AnnoMes(inicioMes.AddMonths(-12));
        int hasta = Claves.AnnoMes(inicioMes); // excluido
        return h.Ventas
            .Where(v => v.AnnoMes >= desde && v.AnnoMes < hasta && !string.IsNullOrWhiteSpace(v.GrupoSubgrupo))
            .GroupBy(v => v.GrupoSubgrupo)
            .Select(g => (Grupo: g.Key, Cantidad: g.Sum(v => v.Cantidad)))
            .Where(g => g.Cantidad > 0)
            .OrderByDescending(g => g.Cantidad).ThenBy(g => g.Grupo, StringComparer.Ordinal)
            .Select(g => g.Grupo)
            .FirstOrDefault() ?? SIN_GRUPO;
    }
}
