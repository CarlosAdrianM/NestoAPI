namespace ModeloLlamadaPedido.Features;

/// <summary>
/// NestoAPI#603 c3: ENTRADA DEL MODELO. NestoAPI#619: es la MISMA clase que usa la API para puntuar (referencia este núcleo),
/// así que solo hay una. No se puede renombrar ni cambiar el tipo de nada sin reentrenar
/// (ML.NET enlaza por nombre de columna). La API calcula cada campo como <see cref="CalculadoraFeatures"/> (CalculadoraFeaturesContacto).
/// Sustituye a ClienteInteraccion. Ya NO hay DiasDesdeUltimaInteraccion ni InteraccionesUltimos12Meses.
/// <para>«Pedido» = día distinto (CabPedidoVta.Fecha) del cliente/contacto, empresa 1, con alguna línea TipoLinea 1,
/// Base Imponible &gt; 0, NotaEntrega 0 y Estado ≥ -1. «d» = día del contacto (hoy en inferencia); todo se mide ANTES de d.</para>
/// </summary>
public class ModeloContactoEntrada
{
    /// <summary>"cliente/contacto" recortados. No es feature (solo para cruzar el resultado).</summary>
    public string ClienteId { get; set; } = "";
    /// <summary>"Llamada" | "Visita" | "WhatsApp" (T/V/W). Categórica.</summary>
    public string TipoInteraccion { get; set; } = "";
    /// <summary>Mes del contacto, "1".."12". Categórica.</summary>
    public string Mes { get; set; } = "";
    /// <summary>Día de la semana ISO, "1" (lunes) .. "7" (domingo). Categórica.</summary>
    public string DiaSemana { get; set; } = "";
    /// <summary>Grupo+SubGrupo (p. ej. "COSCRE") con más cantidad en los 12 meses naturales anteriores al mes de d (sin el mes en curso, sin MMP); empate: orden alfabético; "NADA" si no hay. Categórica.</summary>
    public string GrupoSubgrupoMasVendido { get; set; } = "";
    /// <summary>1 si la hora del contacto es ≥ 15 (hora local), si no 0.</summary>
    public float EsPorLaTarde { get; set; }
    /// <summary>Pedidos (días) en [d − 12 meses, d).</summary>
    public float Pedidos12Meses { get; set; }
    /// <summary>Suma de base imponible de esos pedidos.</summary>
    public float Importe12Meses { get; set; }
    /// <summary>Días entre el último pedido anterior a d (en 24 meses) y d; 1000 si no hay.</summary>
    public float DiasDesdeUltimoPedido { get; set; }
    /// <summary>365 / Pedidos12Meses; 1000 si no hay pedidos en 12 meses.</summary>
    public float DiasEntrePedidos { get; set; }
    /// <summary>Pedidos en [d − 12 meses, d − 11 meses) (el mismo mes del año anterior).</summary>
    public float PedidosMismoMesAnnoAnterior { get; set; }
    /// <summary>Importe12Meses / Pedidos12Meses; 0 si no hay pedidos.</summary>
    public float ImporteMedioPedido { get; set; }
    /// <summary>(A − B) / (A + B) con A = importe en [d − 6 m, d) y B = importe en [d − 12 m, d − 6 m); 0 si A + B = 0. De −1 a 1.</summary>
    public float TendenciaImporte { get; set; }
    /// <summary>Conversión histórica suavizada: (positivos + 2·0,25) / (contactos + 2) sobre los contactos previos del cliente en 24 meses con la ventana de 7 días ya cerrada (fecha + 7 &lt; d). Positivo = Pedido = 1 o pedido en [fecha, fecha + 7].</summary>
    public float TasaConversionCliente { get; set; }
    /// <summary>Contactos previos usados en la tasa (mismo criterio). Da confianza a la tasa.</summary>
    public float ContactosPrevios { get; set; }
    /// <summary>1 si no hay ningún pedido en [d − 24 meses, d); si no 0.</summary>
    public float SinHistorial { get; set; }
}

/// <summary>Una fila del dataset: la entrada del modelo + etiqueta, peso y datos de evaluación (no son features).</summary>
public sealed class FilaEntrenamiento : ModeloContactoEntrada
{
    public bool Label { get; set; }
    public float Peso { get; set; } = 1f;
    public string Vendedor { get; set; } = "";
    public DateTime Fecha { get; set; }
    public int AnnoMes { get; set; }
}

/// <summary>Salida del modelo (LightGBM/FastTree binario calibrado).</summary>
public sealed class PrediccionContacto
{
    public bool PredictedLabel { get; set; }
    public float Score { get; set; }
    public float Probability { get; set; }
}
