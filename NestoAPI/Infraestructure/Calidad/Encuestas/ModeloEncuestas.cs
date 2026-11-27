using System;
using System.Collections.Generic;
using System.Globalization;

namespace NestoAPI.Infraestructure.Calidad.Encuestas
{
    /// <summary>NestoAPI#914 (encuestas, corte E1): los cinco temas del piloto (Carlos 27/11/26). Se guardan por su nombre.</summary>
    public enum TemaEncuesta
    {
        PreparacionEntrega,
        Vendedor,
        ProblemaResuelto,
        AdministracionCobro,
        InformacionTecnica
    }

    /// <summary>
    /// Qué clase de evento dispara la candidata: decide la prioridad cuando compiten varias el mismo día (Carlos 27/11/26):
    /// problema resuelto &gt; un cambio (vendedor, forma de pago) &gt; una primera vez &gt; el resto (el tema por el que hace más
    /// tiempo que no se le pregunta). El número es el orden: menor = antes.
    /// </summary>
    public enum ClaseCandidataEncuesta
    {
        ProblemaResuelto = 0,
        Cambio = 1,
        PrimeraVez = 2,
        Otra = 3
    }

    /// <summary>Por qué el gobernador no se queda con una candidata (el primero que se cumple, en este orden).</summary>
    public enum MotivoDescarteEncuesta
    {
        /// <summary>Tiene baja de comunicaciones (CorreosPostCompraBajas de todo, por cliente o por su correo).</summary>
        Baja,
        /// <summary>Tiene un impagado sin liquidar (ExtractoCliente TipoApunte 4 con ImportePdte ≠ 0, como ModeloImpagados).</summary>
        Impago,
        /// <summary>No han pasado los días entre encuestas desde la última (con la duplicación por ignoradas).</summary>
        TopeEntreEncuestas,
        /// <summary>La última encuesta fue de ese tema, o hubo una de ese tema hace menos de 12 meses (Vendedor: del MISMO vendedor).</summary>
        TemaRepetido,
        /// <summary>Ni app de clientes con avisos ni correo válido.</summary>
        SinCanal,
        /// <summary>Pasaba todo, pero ese día ganó otra candidata del mismo cliente (como mucho una al día).</summary>
        OtraCandidataElegida
    }

    /// <summary>Por dónde se mandaría (solo se calcula en E1; nunca los dos a la vez).</summary>
    public static class CanalesEncuesta
    {
        public const string PUSH = "Push";
        public const string CORREO = "Correo";
        public const string SIN_CANAL = "SinCanal";
    }

    /// <summary>Ámbito del vendedor por el que se pregunta (la regla de ComercialesPedido: PEL = peluquería; el resto, estética).</summary>
    public static class AmbitosVendedorEncuesta
    {
        public const string ESTETICA = "Estetica";
        public const string PELUQUERIA = "Peluqueria";

        /// <summary>«de estética» / «de peluquería» (sin género: Vendedores no dice si es asesor o asesora).</summary>
        public static string Texto(string ambito) =>
            string.Equals(ambito, PELUQUERIA, StringComparison.OrdinalIgnoreCase) ? "de peluquería" : "de estética";
    }

    /// <summary>Una candidata a encuesta de un cliente para un día (lo que lee una <see cref="IFuenteCandidatosEncuesta"/>).</summary>
    public class CandidataEncuesta
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public TemaEncuesta Tema { get; set; }
        public ClaseCandidataEncuesta Clase { get; set; }
        /// <summary>La fuente que la ha dado (nombre de su clase sin «Fuente»), para el desglose.</summary>
        public string Origen { get; set; }
        /// <summary>De usted, por qué se le preguntaría («Recibió el pedido 930001 el 20/11»).</summary>
        public string Motivo { get; set; }
        /// <summary>Cuándo pasó lo que la dispara.</summary>
        public DateTime FechaEvento { get; set; }
        /// <summary>El día en que es candidata (FechaEvento + el retraso de su fuente).</summary>
        public DateTime Dia { get; set; }
        public int? Pedido { get; set; }
        public string Factura { get; set; }
        public int? Envio { get; set; }
        public int? EventoId { get; set; }
        public string Producto { get; set; }
        public string NombreProducto { get; set; }
        /// <summary>Tema Vendedor: el vendedor CONCRETO por el que se pregunta (y al que se avisará de una nota mala en E3).</summary>
        public string Vendedor { get; set; }
        /// <summary>Vendedores.Descripción (el nombre que sale en el correo del pedido, «Vendedor: Paloma Sánchez»).</summary>
        public string NombreVendedor { get; set; }
        /// <summary><see cref="AmbitosVendedorEncuesta"/>.</summary>
        public string AmbitoVendedor { get; set; }
        /// <summary>Información técnica: quién dio el evento (Eventos.Ponente), si consta.</summary>
        public string Profesora { get; set; }
        /// <summary>Lo calcula el servicio antes del gobernador (<see cref="CanalesEncuesta"/>).</summary>
        public string Canal { get; set; }
        /// <summary>El correo al que iría (si el canal es correo).</summary>
        public string Correo { get; set; }

        /// <summary>
        /// La clave de «no repetir tema»: el tema y, en el de Vendedor, el vendedor (Carlos 27/11/26: si le cambian el vendedor se le puede
        /// preguntar por el nuevo aunque hace poco se le preguntara por el anterior).
        /// </summary>
        public string ClaveTema => ClaveDeTema(Tema, Vendedor);

        public static string ClaveDeTema(TemaEncuesta tema, string vendedor) =>
            tema == TemaEncuesta.Vendedor && !string.IsNullOrWhiteSpace(vendedor)
                ? tema.ToString() + "|" + vendedor.Trim().ToUpperInvariant()
                : tema.ToString();

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}/{1} {2} {3:dd/MM/yyyy}", Cliente, Contacto, ClaveTema, Dia);
    }

    /// <summary>Una encuesta ya decidida (en sombra o, desde E2, enviada) de un cliente.</summary>
    public class EncuestaHistorial
    {
        public string Cliente { get; set; }
        public DateTime Dia { get; set; }
        public TemaEncuesta Tema { get; set; }
        public string ClaveTema { get; set; }
        /// <summary>Se mandó y no la contestó (<see cref="ReglaEncuestaIgnorada"/>). En sombra, nunca.</summary>
        public bool Ignorada { get; set; }
    }

    /// <summary>Lo que el gobernador sabe del cliente además de su historial.</summary>
    public class SituacionClienteEncuesta
    {
        public bool DeBaja { get; set; }
        public bool ImpagoEnGestion { get; set; }
    }

    /// <summary>Lo que el gobernador decide para un cliente un día.</summary>
    public class DecisionEncuestaCliente
    {
        public CandidataEncuesta Elegida { get; set; }
        /// <summary>El intervalo que se ha aplicado (con la duplicación por ignoradas).</summary>
        public int IntervaloDias { get; set; }
        public List<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>> Descartes { get; set; } =
            new List<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>>();
    }

    /// <summary>
    /// Los parámetros de ParametrosUsuario «(defecto)» (empresa 1) de las encuestas. Se leen sin crear fila
    /// (<c>LeerParametroSinDefecto</c> con el usuario «(defecto)»); sin fila, el valor por defecto.
    /// </summary>
    public static class ParametrosEncuestas
    {
        /// <summary>Días entre encuestas a un mismo cliente (60 sin fila; nunca menos de <see cref="DIAS_ENTRE_MINIMO"/>).</summary>
        public const string DIAS_ENTRE = "Calidad.EncuestasDiasEntre";
        /// <summary>Interruptor del job en sombra: SOLO «1» enciende; sin fila, apagado.</summary>
        public const string SOMBRA = "Calidad.EncuestasSombra";
        /// <summary>Días desde la entrega de un aparato hasta preguntar por la información técnica (15 sin fila).</summary>
        public const string DIAS_TRAS_APARATO = "Calidad.EncuestasDiasTrasAparato";
        /// <summary>Días desde la factura de una formación sin evento hasta preguntar (7 sin fila).</summary>
        public const string DIAS_TRAS_FORMACION = "Calidad.EncuestasDiasTrasFormacion";

        public const int DIAS_ENTRE_POR_DEFECTO = 60;
        /// <summary>El suelo (Carlos 27/11/26): «nunca menos de 30». Ni el parámetro ni la petición lo bajan.</summary>
        public const int DIAS_ENTRE_MINIMO = 30;
        /// <summary>El techo de lo que se puede pedir o poner.</summary>
        public const int DIAS_ENTRE_MAXIMO = 180;
        public const int DIAS_TRAS_APARATO_POR_DEFECTO = 15;
        public const int DIAS_TRAS_FORMACION_POR_DEFECTO = 7;

        /// <summary>
        /// El intervalo a partir del valor del parámetro: sin fila o ilegible, 60; por debajo del suelo, 30; por encima del techo, 180. Con
        /// <paramref name="aviso"/> de usted cuando el valor no se ha podido usar tal cual (null si se ha usado).
        /// </summary>
        public static int DiasEntreDesdeParametro(string valor, out string aviso)
        {
            aviso = null;
            if (string.IsNullOrWhiteSpace(valor))
            {
                return DIAS_ENTRE_POR_DEFECTO;
            }
            if (!int.TryParse(valor.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dias))
            {
                aviso = $"El parámetro {DIAS_ENTRE} vale «{valor.Trim()}», que no es un número de días: se usan {DIAS_ENTRE_POR_DEFECTO}.";
                return DIAS_ENTRE_POR_DEFECTO;
            }
            if (dias < DIAS_ENTRE_MINIMO)
            {
                aviso = $"El parámetro {DIAS_ENTRE} vale {dias}, por debajo del mínimo de {DIAS_ENTRE_MINIMO} días: se usan {DIAS_ENTRE_MINIMO}.";
                return DIAS_ENTRE_MINIMO;
            }
            if (dias > DIAS_ENTRE_MAXIMO)
            {
                aviso = $"El parámetro {DIAS_ENTRE} vale {dias}, por encima del máximo de {DIAS_ENTRE_MAXIMO} días: se usan {DIAS_ENTRE_MAXIMO}.";
                return DIAS_ENTRE_MAXIMO;
            }
            return dias;
        }

        /// <summary>Un número de días positivo del parámetro, o el de por defecto.</summary>
        public static int DiasDesdeParametro(string valor, int porDefecto) =>
            int.TryParse(valor?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dias) && dias > 0 && dias <= 365 ? dias : porDefecto;

        public static bool Encendido(string valor) => string.Equals(valor?.Trim(), "1", StringComparison.Ordinal);
    }

    /// <summary>Los retrasos de las fuentes que vienen de parámetros (el resto son constantes de cada fuente).</summary>
    public class ParametrosFuentesEncuesta
    {
        public int DiasTrasAparato { get; set; } = ParametrosEncuestas.DIAS_TRAS_APARATO_POR_DEFECTO;
        public int DiasTrasFormacion { get; set; } = ParametrosEncuestas.DIAS_TRAS_FORMACION_POR_DEFECTO;
    }

    /// <summary>
    /// E2: una encuesta enviada cuenta como IGNORADA si pasan <see cref="DIAS_PARA_IGNORADA"/> días sin respuesta. En E1 (sombra) no se envía
    /// nada, así que ninguna lo es. PURA.
    /// </summary>
    public static class ReglaEncuestaIgnorada
    {
        public const int DIAS_PARA_IGNORADA = 14;

        public static bool EsIgnorada(DateTime? enviadaEl, DateTime? respondidaEl, DateTime hoy) =>
            enviadaEl.HasValue && !respondidaEl.HasValue && (hoy.Date - enviadaEl.Value.Date).TotalDays >= DIAS_PARA_IGNORADA;
    }
}
