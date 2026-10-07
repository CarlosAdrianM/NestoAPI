using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#603 (corte 4): cómo va el vendedor respecto a lo esperado a estas alturas (día, semana o mes).</summary>
    public enum AvanceRitmo
    {
        PorDebajo,
        EnLinea,
        PorEncima
    }

    /// <summary>
    /// NestoAPI#603 (corte 4): la situación del vendedor en este momento, calculada de forma determinista a partir del
    /// <see cref="RitmoContactosDTO"/>, el calendario y los contactos de los días anteriores. Elige el grupo de frases
    /// (<see cref="Clave"/>) y da los valores de las variables de las plantillas.
    /// </summary>
    public class SituacionRitmo
    {
        public string Clave { get; set; }
        public bool EsLaborable { get; set; }

        public int Hoy { get; set; }
        public int Semana { get; set; }
        public int Mes { get; set; }
        public int ObjetivoMes { get; set; }
        /// <summary>
        /// Objetivo de hoy tal como estaba al empezar el día: ⌈(ObjetivoMes − contactos del mes antes de hoy) / laborables
        /// que quedan (hoy incluido)⌉. No baja a medida que se llama (el <see cref="RitmoContactosDTO.ObjetivoHoy"/> sí).
        /// </summary>
        public int ObjetivoHoy { get; set; }
        /// <summary>max(0, ObjetivoHoy − Hoy).</summary>
        public int Faltan { get; set; }
        public int Maxima { get; set; }
        public int Alta { get; set; }
        /// <summary>Días laborables seguidos cumpliendo el objetivo (los anteriores a hoy, y hoy si ya está cumplido).</summary>
        public int Racha { get; set; }
        public string Nombre { get; set; }

        public AvanceRitmo EstadoDia { get; set; }
        public AvanceRitmo EstadoSemana { get; set; }
        public AvanceRitmo EstadoMes { get; set; }

        public bool PrimerContactoDelDia { get; set; }
        public bool PrimerPedidoDelDia { get; set; }
        public bool ObjetivoDiaCumplido { get; set; }
        public bool MejorDiaDelMes { get; set; }
        public bool UltimoDiaLaborableMes { get; set; }
        public bool Lunes { get; set; }
        public bool Viernes { get; set; }
        public bool Tarde { get; set; }

        public bool TieneNombre => !string.IsNullOrWhiteSpace(Nombre);
    }

    /// <summary>NestoAPI#603 (corte 4): una plantilla del banco. <see cref="Condicion"/> null = vale siempre.</summary>
    public class PlantillaFraseRitmo
    {
        public PlantillaFraseRitmo(string clave, string situacion, string texto, Func<SituacionRitmo, bool> condicion = null)
        {
            Clave = clave;
            Situacion = situacion;
            Texto = texto;
            Condicion = condicion;
        }

        public string Clave { get; }
        public string Situacion { get; }
        public string Texto { get; }
        public Func<SituacionRitmo, bool> Condicion { get; }

        public bool Vale(SituacionRitmo s) => Condicion == null || Condicion(s);
    }

    /// <summary>NestoAPI#603 (corte 4): los grupos de frases. Uno por situación principal, en orden de prioridad.</summary>
    public static class SituacionesRitmo
    {
        public const string NO_LABORABLE = "NoLaborable";
        public const string MES_CUMPLIDO = "MesCumplido";
        public const string MEJOR_DIA = "MejorDia";
        public const string OBJETIVO_CUMPLIDO = "ObjetivoCumplido";
        public const string PRIMER_PEDIDO = "PrimerPedido";
        public const string PRIMER_CONTACTO = "PrimerContacto";
        public const string ARRANQUE = "Arranque";
        public const string ARRANQUE_LUNES = "ArranqueLunes";
        public const string ARRANQUE_VIERNES = "ArranqueViernes";
        public const string ULTIMO_DIA_MES = "UltimoDiaMes";
        public const string TARDE_APRETAR = "TardeApretar";
        public const string POR_DEBAJO = "PorDebajo";
        public const string BUEN_RITMO = "BuenRitmo";
        public const string EN_LINEA = "EnLinea";
        /// <summary>Comodín: cuando ninguna plantilla del grupo vale (sin condiciones).</summary>
        public const string GENERAL = "General";
    }

    /// <summary>
    /// NestoAPI#603 (corte 4): el banco de plantillas. Tono cercano y motivador, en tuteo, nunca de reproche. Variables:
    /// {hoy}, {objetivoHoy}, {faltan}, {semana}, {mes}, {objetivoMes}, {maxima}, {alta}, {racha} y {nombre}. Con
    /// <c>{variable:palabra}</c> sale el número con la palabra en singular o plural («1 contacto», «3 contactos»). Las
    /// plantillas que escriben el plural a mano llevan la condición de que el número sea mayor que 1.
    /// </summary>
    public static class BancoFrasesRitmo
    {
        public static readonly IReadOnlyList<PlantillaFraseRitmo> Plantillas = new List<PlantillaFraseRitmo>
        {
            // ---- Día no laborable
            new PlantillaFraseRitmo("nl1", SituacionesRitmo.NO_LABORABLE, "Hoy no toca, {nombre}: a descansar. Este mes llevas {mes:contacto} de {objetivoMes}.", s => s.TieneNombre),
            new PlantillaFraseRitmo("nl2", SituacionesRitmo.NO_LABORABLE, "Día de descanso. El próximo laborable te esperan {maxima} clientes de prioridad Máxima y {alta} de Alta.", s => s.Maxima > 1),
            new PlantillaFraseRitmo("nl3", SituacionesRitmo.NO_LABORABLE, "Hoy no es laborable. Llevas {mes:contacto} este mes; el próximo día seguimos con energía."),
            new PlantillaFraseRitmo("nl4", SituacionesRitmo.NO_LABORABLE, "Hoy toca desconectar. La lista te estará esperando el próximo día, ya ordenada."),

            // ---- Objetivo del mes cubierto
            new PlantillaFraseRitmo("mc1", SituacionesRitmo.MES_CUMPLIDO, "¡Objetivo del mes cubierto! {mes:contacto} de {objetivoMes}. Todo lo que sumes ahora es ventaja."),
            new PlantillaFraseRitmo("mc2", SituacionesRitmo.MES_CUMPLIDO, "Ya has hablado con toda la cartera que tocaba este mes ({mes} de {objetivoMes}). ¡Enhorabuena, {nombre}!", s => s.TieneNombre),
            new PlantillaFraseRitmo("mc3", SituacionesRitmo.MES_CUMPLIDO, "Mes cubierto. Si te sobra un rato, los clientes de prioridad Máxima ({maxima}) agradecen una llamada.", s => s.Maxima > 0),
            new PlantillaFraseRitmo("mc4", SituacionesRitmo.MES_CUMPLIDO, "Con {mes:contacto} ya has cubierto el mes. Hoy llevas {hoy}: cada llamada extra es terreno ganado."),

            // ---- Objetivo de hoy cumplido y mejor día del mes
            new PlantillaFraseRitmo("md1", SituacionesRitmo.MEJOR_DIA, "¡Mejor día del mes! {hoy:contacto} hoy y el objetivo de {objetivoHoy} ya está superado."),
            new PlantillaFraseRitmo("md2", SituacionesRitmo.MEJOR_DIA, "Hoy estás batiendo tu marca del mes: {hoy:contacto}. ¡Qué buen día, {nombre}!", s => s.TieneNombre),
            new PlantillaFraseRitmo("md3", SituacionesRitmo.MEJOR_DIA, "{hoy:contacto} hoy, tu mejor marca del mes. Si sigues así, la semana sale redonda."),

            // ---- Objetivo de hoy cumplido
            new PlantillaFraseRitmo("oc1", SituacionesRitmo.OBJETIVO_CUMPLIDO, "¡Objetivo de hoy cumplido! {hoy} de {objetivoHoy}. Lo que venga ahora es para adelantar trabajo."),
            new PlantillaFraseRitmo("oc2", SituacionesRitmo.OBJETIVO_CUMPLIDO, "Hecho: {hoy:contacto} hoy. Cada llamada de más te deja mañana más tranquilo."),
            new PlantillaFraseRitmo("oc3", SituacionesRitmo.OBJETIVO_CUMPLIDO, "Ya tienes los {objetivoHoy} de hoy. Llevas {racha:día} seguidos cumpliendo: ¡vaya racha!", s => s.Racha >= 2),
            new PlantillaFraseRitmo("oc4", SituacionesRitmo.OBJETIVO_CUMPLIDO, "Objetivo del día en el bolsillo. Quedan {maxima:cliente} de prioridad Máxima por si te animas con uno más.", s => s.Maxima > 0),
            new PlantillaFraseRitmo("oc5", SituacionesRitmo.OBJETIVO_CUMPLIDO, "{hoy:contacto} hoy, objetivo cumplido. Buen trabajo, {nombre}.", s => s.TieneNombre),

            // ---- Primer pedido del día
            new PlantillaFraseRitmo("pp1", SituacionesRitmo.PRIMER_PEDIDO, "¡Primer pedido del día! Con ese ánimo, a por {faltan:contacto} más.", s => s.Faltan > 0),
            new PlantillaFraseRitmo("pp2", SituacionesRitmo.PRIMER_PEDIDO, "Ya ha caído el primer pedido de hoy. Las llamadas dan fruto: sigue con la lista."),
            new PlantillaFraseRitmo("pp3", SituacionesRitmo.PRIMER_PEDIDO, "Primer pedido de hoy conseguido. Quedan {maxima:cliente} de prioridad Máxima con muchas papeletas.", s => s.Maxima > 0),

            // ---- Primer contacto del día
            new PlantillaFraseRitmo("pc1", SituacionesRitmo.PRIMER_CONTACTO, "Primer contacto del día hecho: lo más difícil ya está. Te quedan {faltan:contacto} para el objetivo.", s => s.Faltan > 0),
            new PlantillaFraseRitmo("pc2", SituacionesRitmo.PRIMER_CONTACTO, "¡Arrancamos! 1 de {objetivoHoy}. El siguiente sale solo.", s => s.ObjetivoHoy > 1),
            new PlantillaFraseRitmo("pc3", SituacionesRitmo.PRIMER_CONTACTO, "Buen comienzo, {nombre}. Uno hecho; la lista te va guiando por orden.", s => s.TieneNombre),
            new PlantillaFraseRitmo("pc4", SituacionesRitmo.PRIMER_CONTACTO, "El primero ya está. Tira de la prioridad Máxima: son {maxima} y suelen ser llamadas agradecidas.", s => s.Maxima > 1),

            // ---- Arranque (todavía ningún contacto hoy)
            new PlantillaFraseRitmo("a1", SituacionesRitmo.ARRANQUE, "Buenos días, {nombre}. Hoy tocan {objetivoHoy:contacto}; la lista ya está ordenada para empezar por el mejor.", s => s.TieneNombre && s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("a2", SituacionesRitmo.ARRANQUE, "Hoy, {objetivoHoy:contacto} para ir al día. Empieza por los {maxima} de prioridad Máxima: es donde más se nota.", s => s.Maxima > 1 && s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("a3", SituacionesRitmo.ARRANQUE, "Nuevo día, lista nueva: {objetivoHoy:contacto} y la cartera al día. ¡Vamos!", s => s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("a4", SituacionesRitmo.ARRANQUE, "Hoy tocan {objetivoHoy:contacto}. {alta} buenos clientes llevan tiempo esperando tu llamada.", s => s.Alta > 1 && s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("a5", SituacionesRitmo.ARRANQUE, "Esta semana llevas {semana:contacto}. Hoy suma {objetivoHoy} más y seguirás en línea.", s => s.Semana > 0 && s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("a6", SituacionesRitmo.ARRANQUE, "Arrancas con una racha de {racha:día} cumpliendo. Hoy, {objetivoHoy:contacto} para mantenerla.", s => s.Racha >= 2 && s.ObjetivoHoy > 0),

            // ---- Arranque en lunes
            new PlantillaFraseRitmo("l1", SituacionesRitmo.ARRANQUE_LUNES, "Lunes, semana nueva: {objetivoHoy:contacto} hoy para empezar con buen pie.", s => s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("l2", SituacionesRitmo.ARRANQUE_LUNES, "Feliz lunes, {nombre}. La lista empieza por quien más te necesita; hoy tocan {objetivoHoy}.", s => s.TieneNombre && s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("l3", SituacionesRitmo.ARRANQUE_LUNES, "Empieza la semana. Hay {maxima:cliente} de prioridad Máxima esperando: buen lunes para llamarlos.", s => s.Maxima > 0),

            // ---- Arranque en viernes
            new PlantillaFraseRitmo("v1", SituacionesRitmo.ARRANQUE_VIERNES, "Viernes: {objetivoHoy:contacto} y a cerrar la semana bien arriba.", s => s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("v2", SituacionesRitmo.ARRANQUE_VIERNES, "Último empujón de la semana, {nombre}. Llevas {semana}; hoy tocan {objetivoHoy} más.", s => s.TieneNombre && s.ObjetivoHoy > 0),
            new PlantillaFraseRitmo("v3", SituacionesRitmo.ARRANQUE_VIERNES, "Viernes: si hoy llamas a los de prioridad Máxima ({maxima}), el lunes empiezas con ventaja.", s => s.Maxima > 0),

            // ---- Último día laborable del mes
            new PlantillaFraseRitmo("u1", SituacionesRitmo.ULTIMO_DIA_MES, "Último día laborable del mes: {mes} de {objetivoMes}. ¡A por el remate!"),
            new PlantillaFraseRitmo("u2", SituacionesRitmo.ULTIMO_DIA_MES, "Hoy se cierra el mes. Cada contacto cuenta para dejar la cartera bien atendida."),
            new PlantillaFraseRitmo("u3", SituacionesRitmo.ULTIMO_DIA_MES, "Último día del mes, {nombre}: {faltan:contacto} más y cierras con buen sabor de boca.", s => s.TieneNombre && s.Faltan > 0),

            // ---- Por la tarde y todavía por debajo
            new PlantillaFraseRitmo("t1", SituacionesRitmo.TARDE_APRETAR, "La tarde es buen momento para llamar: te quedan {faltan:contacto} para el objetivo de hoy.", s => s.Faltan > 0),
            new PlantillaFraseRitmo("t2", SituacionesRitmo.TARDE_APRETAR, "Queda tarde por delante. {faltan:contacto} más y el día queda redondo.", s => s.Faltan > 0),
            new PlantillaFraseRitmo("t3", SituacionesRitmo.TARDE_APRETAR, "Por la tarde mucha gente contesta con más calma. Te quedan {faltan:contacto} hoy.", s => s.Faltan > 0),
            new PlantillaFraseRitmo("t4", SituacionesRitmo.TARDE_APRETAR, "Un último tirón: {hoy} de {objetivoHoy} hoy. Empieza por la prioridad Máxima y verás cómo sube.", s => s.Maxima > 0),

            // ---- Por debajo de lo esperado a esta hora
            new PlantillaFraseRitmo("d1", SituacionesRitmo.POR_DEBAJO, "Llevas {hoy} de {objetivoHoy} hoy. Un par de llamadas seguidas y te pones en línea."),
            new PlantillaFraseRitmo("d2", SituacionesRitmo.POR_DEBAJO, "Quedan {faltan:contacto} para el objetivo de hoy. La lista ya te dice por quién seguir.", s => s.Faltan > 0),
            new PlantillaFraseRitmo("d3", SituacionesRitmo.POR_DEBAJO, "Hay {maxima:cliente} de prioridad Máxima con mucha probabilidad de pedido. Buen sitio para la próxima llamada.", s => s.Maxima > 0),
            new PlantillaFraseRitmo("d4", SituacionesRitmo.POR_DEBAJO, "{alta} buenos clientes esperan tu llamada. Con {faltan} más hoy vas al día.", s => s.Alta > 1 && s.Faltan > 0),
            new PlantillaFraseRitmo("d5", SituacionesRitmo.POR_DEBAJO, "Vamos a por ello, {nombre}: {faltan:contacto} y el día está hecho.", s => s.TieneNombre && s.Faltan > 0),
            new PlantillaFraseRitmo("d6", SituacionesRitmo.POR_DEBAJO, "Este mes llevas {mes} de {objetivoMes}. Hoy, {faltan:contacto} más para seguir sumando.", s => s.Faltan > 0),

            // ---- Por delante del calendario (semana o mes)
            new PlantillaFraseRitmo("b1", SituacionesRitmo.BUEN_RITMO, "Vas por delante este mes: {mes} de {objetivoMes}. ¡Así da gusto!"),
            new PlantillaFraseRitmo("b2", SituacionesRitmo.BUEN_RITMO, "Buen ritmo esta semana: {semana:contacto}. Sigue la lista y el mes sale solo."),
            new PlantillaFraseRitmo("b3", SituacionesRitmo.BUEN_RITMO, "Vas con margen, {nombre}. Aprovecha para llamar a los {maxima} de prioridad Máxima.", s => s.TieneNombre && s.Maxima > 1),
            new PlantillaFraseRitmo("b4", SituacionesRitmo.BUEN_RITMO, "Hoy {hoy} de {objetivoHoy} y el mes por delante del calendario. Muy bien."),

            // ---- En línea
            new PlantillaFraseRitmo("e1", SituacionesRitmo.EN_LINEA, "Vas en línea: {hoy} de {objetivoHoy} hoy. Mantén el ritmo y el día sale solo."),
            new PlantillaFraseRitmo("e2", SituacionesRitmo.EN_LINEA, "Todo en orden: {semana:contacto} esta semana. Siguiente de la lista, ¡y a por ello!"),
            new PlantillaFraseRitmo("e3", SituacionesRitmo.EN_LINEA, "Al día con la cartera. Quedan {faltan:contacto} hoy para dejarlo cerrado.", s => s.Faltan > 0),
            new PlantillaFraseRitmo("e4", SituacionesRitmo.EN_LINEA, "Buen paso, {nombre}. {hoy:contacto} hoy y la lista te espera con los siguientes.", s => s.TieneNombre),
            new PlantillaFraseRitmo("e5", SituacionesRitmo.EN_LINEA, "Llevas {racha:día} seguidos cumpliendo el objetivo. Hoy {hoy} de {objetivoHoy}: ¡a por otro!", s => s.Racha >= 2),

            // ---- Comodín
            new PlantillaFraseRitmo("g1", SituacionesRitmo.GENERAL, "Llevas {hoy:contacto} hoy y {mes} este mes. La lista te dice por quién seguir."),
            new PlantillaFraseRitmo("g2", SituacionesRitmo.GENERAL, "Cada llamada cuenta: {hoy} hoy, {semana} esta semana. ¡Seguimos!"),
            new PlantillaFraseRitmo("g3", SituacionesRitmo.GENERAL, "Tu cartera te espera: empieza por arriba de la lista y verás cómo cunde.")
        };
    }

    /// <summary>
    /// NestoAPI#603 (corte 4): todo lo que no toca BD ni OpenAI de las frases de ritmo. Calcula la
    /// <see cref="SituacionRitmo"/>, elige plantilla sin repetir las últimas, sustituye las variables, valida la frase
    /// de OpenAI y monta su prompt.
    /// </summary>
    public static class GeneradorFrasesRitmo
    {
        public const int LONGITUD_MAXIMA = 160;
        public const int FRASES_EN_MEMORIA = 10;
        /// <summary>La jornada que se usa para saber cuánto debería llevar hecho a esta hora.</summary>
        public const int HORA_INICIO_JORNADA = 9;
        public const int HORA_FIN_JORNADA = 18;
        public const int HORA_TARDE = 15;
        /// <summary>Margen (±10 %) para considerar que va en línea en la semana y el mes.</summary>
        public const double TOLERANCIA = 0.10;
        public const string PLANTILLA_OPENAI = "OpenAI";

        public static readonly IReadOnlyList<string> Variables = new[]
        {
            "hoy", "objetivoHoy", "faltan", "semana", "mes", "objetivoMes", "maxima", "alta", "racha", "nombre"
        };

        private static readonly Regex regexVariable = new Regex(@"\{(\w+)(?::([^{}]+))?\}", RegexOptions.Compiled);
        private static readonly CultureInfo es = new CultureInfo("es-ES");

        // ---------------- Situación ----------------

        /// <param name="contactosPorDia">Contactos (rapports Estado 0 T/V/W) de los días ANTERIORES a hoy; los días sin
        /// contactos pueden faltar.</param>
        public static SituacionRitmo CalcularSituacion(RitmoContactosDTO ritmo, DateTime ahora, Func<DateTime, bool> esLaborable,
            IDictionary<DateTime, int> contactosPorDia, int pedidosHoy, string nombre)
        {
            ritmo = ritmo ?? new RitmoContactosDTO();
            contactosPorDia = contactosPorDia ?? new Dictionary<DateTime, int>();
            Func<DateTime, bool> laborable = d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday && esLaborable(d);
            DateTime hoy = ahora.Date;
            bool esLaborableHoy = laborable(hoy);

            int laborablesMes = MotorSugerenciasContacto.DiasLaborablesDelMes(hoy, esLaborable);
            int restantes = MotorSugerenciasContacto.DiasLaborablesRestantes(hoy, esLaborable);
            int contactosAntesDeHoy = Math.Max(0, ritmo.ContactosMes - ritmo.ContactosHoy);
            int objetivoHoy = esLaborableHoy && restantes > 0
                ? (int)Math.Ceiling((double)Math.Max(0, ritmo.ObjetivoMes - contactosAntesDeHoy) / restantes)
                : 0;
            double fraccionDia = esLaborableHoy ? FraccionJornada(ahora) : 0;
            double ritmoDiario = laborablesMes > 0 ? (double)ritmo.ObjetivoMes / laborablesMes : 0;

            var s = new SituacionRitmo
            {
                EsLaborable = esLaborableHoy,
                Hoy = ritmo.ContactosHoy,
                Semana = ritmo.ContactosSemana,
                Mes = ritmo.ContactosMes,
                ObjetivoMes = ritmo.ObjetivoMes,
                ObjetivoHoy = objetivoHoy,
                Faltan = Math.Max(0, objetivoHoy - ritmo.ContactosHoy),
                Maxima = ritmo.PendientesMaxima,
                Alta = ritmo.PendientesAlta,
                Nombre = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim(),
                Lunes = hoy.DayOfWeek == DayOfWeek.Monday,
                Viernes = hoy.DayOfWeek == DayOfWeek.Friday,
                Tarde = ahora.Hour >= HORA_TARDE,
                PrimerContactoDelDia = ritmo.ContactosHoy == 1,
                PrimerPedidoDelDia = pedidosHoy == 1,
                ObjetivoDiaCumplido = esLaborableHoy && objetivoHoy > 0 && ritmo.ContactosHoy >= objetivoHoy,
                UltimoDiaLaborableMes = esLaborableHoy && restantes == 1
            };

            // Día: lo esperado a esta hora es la parte proporcional de la jornada.
            if (objetivoHoy == 0)
            {
                s.EstadoDia = ritmo.ContactosHoy > 0 ? AvanceRitmo.PorEncima : AvanceRitmo.EnLinea;
            }
            else if (ritmo.ContactosHoy > objetivoHoy)
            {
                s.EstadoDia = AvanceRitmo.PorEncima;
            }
            else
            {
                s.EstadoDia = ritmo.ContactosHoy >= Math.Floor(objetivoHoy * fraccionDia) ? AvanceRitmo.EnLinea : AvanceRitmo.PorDebajo;
            }

            // Semana: % del objetivo frente a % de días laborables consumidos (de lunes a viernes de esta semana).
            DateTime lunes = RepositorioCarteraContactoSql.InicioSemana(hoy);
            int laborablesSemana = Enumerable.Range(0, 5).Count(i => laborable(lunes.AddDays(i)));
            double consumidosSemana = Enumerable.Range(0, 5).Select(i => lunes.AddDays(i)).Count(d => d < hoy && laborable(d)) + fraccionDia;
            s.EstadoSemana = Avance(ritmo.ContactosSemana, ritmoDiario * consumidosSemana);

            // Mes: ídem del día 1 a hoy.
            DateTime primero = new DateTime(hoy.Year, hoy.Month, 1);
            double consumidosMes = laborablesMes - restantes + fraccionDia;
            s.EstadoMes = laborablesMes > 0
                ? Avance(ritmo.ContactosMes, ritmo.ObjetivoMes * consumidosMes / laborablesMes)
                : AvanceRitmo.EnLinea;

            // Racha: días laborables seguidos (hacia atrás desde ayer) con al menos el objetivo diario medio del mes.
            int objetivoDiarioMedio = Math.Max(1, (int)Math.Ceiling(ritmoDiario));
            int racha = 0;
            if (ritmo.ObjetivoMes > 0)
            {
                DateTime dia = hoy.AddDays(-1);
                for (int i = 0; i < 62; i++, dia = dia.AddDays(-1))
                {
                    if (!laborable(dia))
                    {
                        continue;
                    }
                    if (!contactosPorDia.TryGetValue(dia.Date, out int n) || n < objetivoDiarioMedio)
                    {
                        break;
                    }
                    racha++;
                }
                if (s.ObjetivoDiaCumplido)
                {
                    racha++;
                }
            }
            s.Racha = racha;

            // Mejor día del mes: más contactos hoy que cualquier día anterior del mes (que tuvo alguno).
            int mejorAnterior = contactosPorDia.Where(kv => kv.Key >= primero && kv.Key < hoy).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            s.MejorDiaDelMes = esLaborableHoy && mejorAnterior > 0 && ritmo.ContactosHoy > mejorAnterior;

            s.Clave = ClaveSituacion(s);
            return s;
        }

        internal static double FraccionJornada(DateTime ahora)
        {
            double horas = ahora.TimeOfDay.TotalHours - HORA_INICIO_JORNADA;
            double total = HORA_FIN_JORNADA - HORA_INICIO_JORNADA;
            return Math.Max(0, Math.Min(1, horas / total));
        }

        internal static AvanceRitmo Avance(double real, double esperado)
        {
            if (esperado <= 0)
            {
                return real > 0 ? AvanceRitmo.PorEncima : AvanceRitmo.EnLinea;
            }
            double ratio = real / esperado;
            return ratio < 1 - TOLERANCIA ? AvanceRitmo.PorDebajo
                : ratio > 1 + TOLERANCIA ? AvanceRitmo.PorEncima
                : AvanceRitmo.EnLinea;
        }

        /// <summary>La situación principal, por orden de prioridad (el hito más llamativo manda).</summary>
        internal static string ClaveSituacion(SituacionRitmo s)
        {
            if (!s.EsLaborable)
            {
                return SituacionesRitmo.NO_LABORABLE;
            }
            if (s.ObjetivoMes > 0 && s.Mes >= s.ObjetivoMes)
            {
                return SituacionesRitmo.MES_CUMPLIDO;
            }
            if (s.ObjetivoDiaCumplido && s.MejorDiaDelMes)
            {
                return SituacionesRitmo.MEJOR_DIA;
            }
            if (s.ObjetivoDiaCumplido)
            {
                return SituacionesRitmo.OBJETIVO_CUMPLIDO;
            }
            if (s.PrimerPedidoDelDia)
            {
                return SituacionesRitmo.PRIMER_PEDIDO;
            }
            if (s.PrimerContactoDelDia)
            {
                return SituacionesRitmo.PRIMER_CONTACTO;
            }
            if (s.Hoy == 0)
            {
                return s.UltimoDiaLaborableMes ? SituacionesRitmo.ULTIMO_DIA_MES
                    : s.Lunes ? SituacionesRitmo.ARRANQUE_LUNES
                    : s.Viernes ? SituacionesRitmo.ARRANQUE_VIERNES
                    : SituacionesRitmo.ARRANQUE;
            }
            if (s.UltimoDiaLaborableMes)
            {
                return SituacionesRitmo.ULTIMO_DIA_MES;
            }
            if (s.EstadoDia == AvanceRitmo.PorDebajo)
            {
                return s.Tarde ? SituacionesRitmo.TARDE_APRETAR : SituacionesRitmo.POR_DEBAJO;
            }
            if (s.EstadoMes == AvanceRitmo.PorEncima || s.EstadoSemana == AvanceRitmo.PorEncima)
            {
                return SituacionesRitmo.BUEN_RITMO;
            }
            return SituacionesRitmo.EN_LINEA;
        }

        // ---------------- Elección de plantilla ----------------

        /// <summary>Las plantillas del grupo de la situación que valen ahora; si no vale ninguna, las del comodín.</summary>
        public static List<PlantillaFraseRitmo> Candidatas(SituacionRitmo s, IEnumerable<PlantillaFraseRitmo> banco = null)
        {
            banco = banco ?? BancoFrasesRitmo.Plantillas;
            List<PlantillaFraseRitmo> candidatas = banco.Where(p => p.Situacion == s.Clave && p.Vale(s)).ToList();
            return candidatas.Any() ? candidatas : banco.Where(p => p.Situacion == SituacionesRitmo.GENERAL && p.Vale(s)).ToList();
        }

        /// <summary>
        /// Elige plantilla sin repetir ninguna de las <paramref name="recientes"/> (claves, de la más nueva a la más
        /// vieja). Entre las que no se han usado, una fija por vendedor, día y situación (para que no cambie en cada
        /// refresco). Si todas se han usado, la que lleva más tiempo sin salir.
        /// </summary>
        public static PlantillaFraseRitmo Elegir(SituacionRitmo s, IList<string> recientes, string vendedor, DateTime hoy,
            IEnumerable<PlantillaFraseRitmo> banco = null)
        {
            recientes = recientes ?? new List<string>();
            List<PlantillaFraseRitmo> candidatas = Candidatas(s, banco);
            if (!candidatas.Any())
            {
                return null;
            }
            List<PlantillaFraseRitmo> libres = candidatas.Where(p => !recientes.Contains(p.Clave)).ToList();
            if (libres.Any())
            {
                uint semilla = Fnv($"{vendedor?.Trim().ToUpperInvariant()}|{hoy:yyyyMMdd}|{s.Clave}");
                return libres[(int)(semilla % (uint)libres.Count)];
            }
            return candidatas.OrderByDescending(p => recientes.IndexOf(p.Clave)).First();
        }

        private static uint Fnv(string texto)
        {
            uint hash = 2166136261;
            foreach (char c in texto)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }

        // ---------------- Variables ----------------

        public static string Sustituir(string plantilla, SituacionRitmo s)
        {
            if (string.IsNullOrEmpty(plantilla))
            {
                return plantilla;
            }
            return regexVariable.Replace(plantilla, m =>
            {
                string variable = m.Groups[1].Value;
                string palabra = m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
                if (variable == "nombre")
                {
                    return s.Nombre ?? string.Empty;
                }
                int? valor = Valor(variable, s);
                if (!valor.HasValue)
                {
                    return m.Value;
                }
                string numero = valor.Value.ToString(es);
                return palabra == null ? numero : $"{numero} {(valor.Value == 1 ? palabra : Plural(palabra))}";
            }).Replace(" ,", ",").Trim();
        }

        private static int? Valor(string variable, SituacionRitmo s)
        {
            switch (variable)
            {
                case "hoy": return s.Hoy;
                case "objetivoHoy": return s.ObjetivoHoy;
                case "faltan": return s.Faltan;
                case "semana": return s.Semana;
                case "mes": return s.Mes;
                case "objetivoMes": return s.ObjetivoMes;
                case "maxima": return s.Maxima;
                case "alta": return s.Alta;
                case "racha": return s.Racha;
                default: return null;
            }
        }

        internal static string Plural(string palabra)
        {
            if (string.IsNullOrEmpty(palabra))
            {
                return palabra;
            }
            char ultima = char.ToLowerInvariant(palabra[palabra.Length - 1]);
            return "aeiouáéíóú".IndexOf(ultima) >= 0 ? palabra + "s" : palabra + "es";
        }

        /// <summary>Primer nombre con mayúscula inicial («MARÍA JOSÉ DEL POZO» → «María»).</summary>
        public static string NombreDePila(string descripcion)
        {
            if (string.IsNullOrWhiteSpace(descripcion))
            {
                return null;
            }
            string primero = descripcion.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return primero.Length == 1 ? primero.ToUpper(es) : char.ToUpper(primero[0], es) + primero.Substring(1).ToLower(es);
        }

        // ---------------- OpenAI ----------------

        public const string PROMPT_SISTEMA_OPENAI =
            "Escribes UNA frase corta para la cabecera de la lista de clientes a los que un comercial de una distribuidora de " +
            "peluquería y estética tiene que llamar hoy. El objetivo es animarle a hacer sus contactos del día. Tono cercano, " +
            "motivador y natural, en español de España y tuteando; nunca reproches ni culpa, nada de frases hechas de manual. " +
            "Como mucho un emoji (mejor ninguno). Máximo 160 caracteres. NO escribas números: usa en su lugar, tal cual entre " +
            "llaves, las variables que te dan (se sustituyen por el dato del momento); con {variable:palabra} sale el número con " +
            "la palabra en singular o plural, por ejemplo {faltan:contacto}. No uses otras variables. No repitas ni imites las " +
            "frases recientes. Devuelve solo la frase, sin comillas.";

        public static string MensajeOpenAI(SituacionRitmo s, IEnumerable<string> frasesRecientes)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Situación principal: {DescripcionSituacion(s.Clave)}.");
            sb.AppendLine("Variables disponibles (valor actual):");
            sb.AppendLine($"- {{hoy}} contactos hoy: {s.Hoy}");
            sb.AppendLine($"- {{objetivoHoy}} objetivo de contactos de hoy: {s.ObjetivoHoy}");
            sb.AppendLine($"- {{faltan}} contactos que faltan hoy: {s.Faltan}");
            sb.AppendLine($"- {{semana}} contactos esta semana: {s.Semana}");
            sb.AppendLine($"- {{mes}} contactos este mes: {s.Mes}");
            sb.AppendLine($"- {{objetivoMes}} objetivo del mes: {s.ObjetivoMes}");
            sb.AppendLine($"- {{maxima}} clientes de prioridad Máxima esperando: {s.Maxima}");
            sb.AppendLine($"- {{alta}} clientes de prioridad Alta esperando: {s.Alta}");
            sb.AppendLine($"- {{racha}} días seguidos cumpliendo el objetivo: {s.Racha}");
            if (s.TieneNombre)
            {
                sb.AppendLine($"- {{nombre}} nombre del comercial: {s.Nombre}");
            }
            sb.AppendLine($"Día: {Texto(s.EstadoDia)} de lo esperado a esta hora. Semana: {Texto(s.EstadoSemana)}. Mes: {Texto(s.EstadoMes)}.");
            var hitos = new List<string>();
            if (s.Lunes) { hitos.Add("es lunes"); }
            if (s.Viernes) { hitos.Add("es viernes"); }
            if (s.Tarde) { hitos.Add("es por la tarde"); }
            if (s.UltimoDiaLaborableMes) { hitos.Add("es el último día laborable del mes"); }
            if (s.MejorDiaDelMes) { hitos.Add("está siendo su mejor día del mes"); }
            if (hitos.Any())
            {
                sb.AppendLine("Además: " + string.Join(", ", hitos) + ".");
            }
            List<string> recientes = (frasesRecientes ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            if (recientes.Any())
            {
                sb.AppendLine("Frases recientes (no las repitas):");
                foreach (string frase in recientes)
                {
                    sb.AppendLine("- " + frase);
                }
            }
            return sb.ToString();
        }

        private static string Texto(AvanceRitmo avance) =>
            avance == AvanceRitmo.PorDebajo ? "por debajo" : avance == AvanceRitmo.PorEncima ? "por encima" : "en línea";

        private static string DescripcionSituacion(string clave)
        {
            switch (clave)
            {
                case SituacionesRitmo.NO_LABORABLE: return "hoy no es laborable";
                case SituacionesRitmo.MES_CUMPLIDO: return "ya ha cubierto el objetivo del mes";
                case SituacionesRitmo.MEJOR_DIA: return "ha cumplido el objetivo de hoy y es su mejor día del mes";
                case SituacionesRitmo.OBJETIVO_CUMPLIDO: return "ha cumplido el objetivo de hoy";
                case SituacionesRitmo.PRIMER_PEDIDO: return "acaba de conseguir el primer pedido del día";
                case SituacionesRitmo.PRIMER_CONTACTO: return "acaba de hacer el primer contacto del día";
                case SituacionesRitmo.ARRANQUE: return "empieza el día, todavía sin contactos";
                case SituacionesRitmo.ARRANQUE_LUNES: return "empieza la semana (lunes), todavía sin contactos";
                case SituacionesRitmo.ARRANQUE_VIERNES: return "empieza el viernes, todavía sin contactos";
                case SituacionesRitmo.ULTIMO_DIA_MES: return "último día laborable del mes";
                case SituacionesRitmo.TARDE_APRETAR: return "por la tarde y aún le faltan contactos para el objetivo de hoy";
                case SituacionesRitmo.POR_DEBAJO: return "va algo por detrás de lo esperado a esta hora";
                case SituacionesRitmo.BUEN_RITMO: return "va por delante del calendario";
                default: return "va en línea con el objetivo";
            }
        }

        /// <summary>
        /// La frase de OpenAI limpia (sin comillas ni saltos de línea), o null si no vale: vacía, con variables que no
        /// existen, con números escritos (se quedarían viejos), más larga de <see cref="LONGITUD_MAXIMA"/> una vez
        /// sustituida o igual a una reciente.
        /// </summary>
        public static string ValidarFraseOpenAI(string respuesta, SituacionRitmo s, IEnumerable<string> frasesRecientes)
        {
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                return null;
            }
            string frase = Regex.Replace(respuesta, @"\s+", " ").Trim().Trim('"', '«', '»', '“', '”', '\'').Trim();
            if (frase.Length == 0)
            {
                return null;
            }
            foreach (Match m in regexVariable.Matches(frase))
            {
                if (!Variables.Contains(m.Groups[1].Value) || (m.Groups[1].Value == "nombre" && !s.TieneNombre))
                {
                    return null;
                }
            }
            if (Regex.IsMatch(regexVariable.Replace(frase, string.Empty), @"\d"))
            {
                return null;
            }
            if (frase.IndexOf('{') >= 0 && !regexVariable.IsMatch(frase))
            {
                return null;
            }
            if (Sustituir(frase, s).Length > LONGITUD_MAXIMA)
            {
                return null;
            }
            if ((frasesRecientes ?? Enumerable.Empty<string>()).Any(r => string.Equals(r?.Trim(), frase, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            return frase;
        }
    }
}
