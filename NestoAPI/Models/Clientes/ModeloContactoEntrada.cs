namespace NestoAPI.Models.Clientes
{
    /// <summary>
    /// NestoAPI#603 c3b: entrada del modelo de contactos (ModelsIA/modelo_llamadas.zip). Es copia EXACTA (nombres y tipos)
    /// de ModeloLlamadaPedido\Features\ModeloContactoEntrada.cs: ML.NET enlaza las columnas por nombre, así que no se puede
    /// renombrar nada sin reentrenar. Cada campo se calcula como en el entrenamiento (<see cref="Infraestructure.Rapports.CalculadoraFeaturesContacto"/>),
    /// con «d» = hoy.
    /// </summary>
    public class ModeloContactoEntrada
    {
        /// <summary>"cliente/contacto" recortados. No es feature (solo para cruzar el resultado).</summary>
        public string ClienteId { get; set; } = "";
        /// <summary>"Llamada" | "Visita" | "WhatsApp". Categórica.</summary>
        public string TipoInteraccion { get; set; } = "";
        /// <summary>Mes, "1".."12". Categórica.</summary>
        public string Mes { get; set; } = "";
        /// <summary>Día de la semana ISO, "1" (lunes) .. "7" (domingo). Categórica.</summary>
        public string DiaSemana { get; set; } = "";
        /// <summary>Grupo+SubGrupo con más cantidad en los 12 meses naturales anteriores al mes en curso (sin MMP); "NADA" si no hay. Categórica.</summary>
        public string GrupoSubgrupoMasVendido { get; set; } = "";
        /// <summary>1 si la hora es ≥ 15.</summary>
        public float EsPorLaTarde { get; set; }
        /// <summary>Pedidos (días) en [d − 12 meses, d).</summary>
        public float Pedidos12Meses { get; set; }
        /// <summary>Base imponible de esos pedidos.</summary>
        public float Importe12Meses { get; set; }
        /// <summary>Días desde el último pedido de los 24 meses; 1000 si no hay.</summary>
        public float DiasDesdeUltimoPedido { get; set; }
        /// <summary>365 / Pedidos12Meses; 1000 si no hay pedidos en 12 meses.</summary>
        public float DiasEntrePedidos { get; set; }
        /// <summary>Pedidos en [d − 12 meses, d − 11 meses).</summary>
        public float PedidosMismoMesAnnoAnterior { get; set; }
        /// <summary>Importe12Meses / Pedidos12Meses; 0 si no hay pedidos.</summary>
        public float ImporteMedioPedido { get; set; }
        /// <summary>(A − B) / (A + B), A = importe en [d − 6 m, d), B = [d − 12 m, d − 6 m); 0 si A + B = 0.</summary>
        public float TendenciaImporte { get; set; }
        /// <summary>(positivos + 0,5) / (contactos + 2) sobre los contactos de 24 meses con la ventana de 7 días cerrada.</summary>
        public float TasaConversionCliente { get; set; }
        /// <summary>Contactos usados en la tasa.</summary>
        public float ContactosPrevios { get; set; }
        /// <summary>1 si no hay ningún pedido en [d − 24 meses, d).</summary>
        public float SinHistorial { get; set; }
    }
}
