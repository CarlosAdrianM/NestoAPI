using NestoAPI.Infraestructure.OpenAI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: las tres piezas del correo de reposición que cambian cada semana (asunto, párrafo de entrada y cierre),
    /// con sus marcadores. Lo demás (saludo, lista de productos con foto y enlace, frase de baja y firma) lo pone SIEMPRE
    /// <see cref="PlantillaRecordatorioReposicion"/>, nunca OpenAI.
    /// </summary>
    public class TextoRecordatorioReposicion
    {
        /// <summary>En el asunto: el primer producto («champú reparador»; con varios, «champú reparador y algo más»).</summary>
        public const string MARCA_PRODUCTO = "{PRODUCTO}";
        /// <summary>En la entrada: «este producto» o «alguno de estos productos».</summary>
        public const string MARCA_PRODUCTOS = "{PRODUCTOS}";
        /// <summary>En el cierre: el nombre de pila del comercial o, si no tiene, «nuestro equipo».</summary>
        public const string MARCA_VENDEDOR = "{VENDEDOR}";

        public const string ORIGEN_FIJA = "Fija";
        public const string ORIGEN_OPENAI = "OpenAI";
        public const string VERSION_FIJA = "fija-1";

        public string Asunto { get; set; }
        public string Entrada { get; set; }
        public string Cierre { get; set; }
        /// <summary><see cref="ORIGEN_FIJA"/> u <see cref="ORIGEN_OPENAI"/>.</summary>
        public string Origen { get; set; }
        /// <summary>Lo que se guarda en cada envío: «fija-1» o «ia-2026-10-22» (la semana).</summary>
        public string Version { get; set; }
        /// <summary>Si es la fija porque OpenAI ha fallado o no ha valido, por qué. Null si no.</summary>
        public string MotivoFija { get; set; }

        /// <summary>El texto base aprobado por Carlos (22/10/26). Es el de respaldo de todas las semanas.</summary>
        public static TextoRecordatorioReposicion Fija(string motivo = null) => new TextoRecordatorioReposicion
        {
            Asunto = "Que no le falte " + MARCA_PRODUCTO + " en mitad de un servicio",
            Entrada = "Le escribimos con un pequeño aviso: es posible que dentro de poco se le acabe " + MARCA_PRODUCTOS +
                ". Preferimos avisarle con tiempo, para que no tenga que descubrirlo con un cliente ya delante:",
            Cierre = "Si todavía tiene de sobra, olvide este correo. Y si le hace falta, puede pedirlo desde la tienda o a " +
                MARCA_VENDEDOR + ", como siempre.",
            Origen = ORIGEN_FIJA,
            Version = VERSION_FIJA,
            MotivoFija = motivo
        };
    }

    public interface IGeneradorTextoRecordatorioReposicion
    {
        /// <summary>El texto de la semana. Nunca null: si OpenAI falla o no vale, la plantilla fija.</summary>
        Task<TextoRecordatorioReposicion> Generar(DateTime semana);
    }

    /// <summary>
    /// NestoAPI#532: como el correo posventa de los sábados (GeneradorContenidoCorreoPostCompra.GenerarPlantillaSemanalAsync),
    /// UNA llamada a OpenAI por semana que escribe asunto, entrada y cierre con los marcadores. Todo lo que devuelve pasa por
    /// <see cref="MotivoNoValido"/>: sin marcadores, con tuteo, con palabras vetadas o largo, se usa la fija y se loguea a ELMAH.
    /// </summary>
    public class GeneradorTextoRecordatorioReposicion : IGeneradorTextoRecordatorioReposicion
    {
        internal const int MAXIMO_ASUNTO = 80;
        internal const int MAXIMO_ENTRADA = 320;
        internal const int MAXIMO_CIERRE = 260;

        /// <summary>
        /// Formas de tú que no tienen lectura de usted. Se dejan fuera las que también lo son («pide», «recuerda», «olvida»:
        /// «si lo pide hoy», «este correo le recuerda»). «te» y «tu» sin tilde son siempre de tú («té» y «tú» llevan tilde
        /// y «tú» también es tuteo: va en la lista).
        /// </summary>
        private static readonly Regex Tuteo = new Regex(
            @"(?<![\p{L}])(tu|tus|tú|te|ti|contigo|tuyo|tuya|tuyos|tuyas|puedes|tienes|quieres|necesitas|sueles|estás|vas|haces|" +
            @"pídelo|pídenos|pídeselo|escríbenos|avísanos|llámanos|olvídate|olvídalo|échale|mira|fíjate)(?![\p{L}])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Lo que no puede decir: que miramos lo que pide («suele», «historial»...), el puesto concreto (vale para estética,
        /// manicura y peluquería) y nada de venta ni urgencia comercial.
        /// </summary>
        internal static readonly string[] Vetadas =
        {
            "suele", "solemos", "historial", "compras anteriores", "sus compras", "sus pedidos", "hemos visto", "hemos notado",
            "camilla", "mesa", "tocador", "sillón", "cabina", "lavacabezas",
            "oferta", "descuento", "promoción", "rebaja", "gratis", "regalo", "urgente", "últimas unidades", "no se lo pierda",
            "aproveche", "date prisa", "solo hoy", "http", "www."
        };

        private readonly IServicioOpenAI servicioOpenAI;
        private readonly Action<Exception> registrarError;

        public GeneradorTextoRecordatorioReposicion(IServicioOpenAI servicioOpenAI, Action<Exception> registrarError = null)
        {
            this.servicioOpenAI = servicioOpenAI;
            this.registrarError = registrarError ?? ElmahHelper.Log;
        }

        public async Task<TextoRecordatorioReposicion> Generar(DateTime semana)
        {
            string respuesta;
            try
            {
                respuesta = servicioOpenAI == null
                    ? null
                    : await servicioOpenAI.GenerarContenidoAsync(SYSTEM_PROMPT,
                        $"Escribe la variante de la semana del {semana:dd/MM/yyyy}. Que no se parezca a la de ejemplo más de lo necesario.",
                        maxTokens: 400, temperature: 0.8).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Fija("OpenAI ha fallado: " + ex.Message, ex);
            }

            TextoRecordatorioReposicion texto = Interpretar(respuesta);
            if (texto == null)
            {
                return Fija("OpenAI no ha devuelto un JSON con asunto, entrada y cierre: " + Recortar(respuesta), null);
            }
            string motivo = MotivoNoValido(texto);
            if (motivo != null)
            {
                return Fija($"El texto de OpenAI no vale ({motivo}): {Recortar(respuesta)}", null);
            }
            texto.Origen = TextoRecordatorioReposicion.ORIGEN_OPENAI;
            texto.Version = "ia-" + semana.ToString("yyyy-MM-dd");
            return texto;
        }

        private TextoRecordatorioReposicion Fija(string motivo, Exception ex)
        {
            try
            {
                registrarError(new Exception("[Recordatorio reposición #532] Se usa el texto fijo. " + motivo, ex));
            }
            catch
            {
                // El log nunca impide mandar el correo
            }
            return TextoRecordatorioReposicion.Fija(motivo);
        }

        private static string Recortar(string texto)
            => texto == null ? "(nada)" : texto.Length > 300 ? texto.Substring(0, 300) + "…" : texto;

        /// <summary>El JSON de la respuesta (con o sin ```json alrededor). Null si no lo hay o le falta alguna pieza.</summary>
        internal static TextoRecordatorioReposicion Interpretar(string respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                return null;
            }
            int inicio = respuesta.IndexOf('{');
            int fin = respuesta.LastIndexOf('}');
            // El JSON empieza en la primera llave que abre un objeto: «{"»
            int objeto = respuesta.IndexOf("{\"", StringComparison.Ordinal);
            if (objeto >= 0)
            {
                inicio = objeto;
            }
            if (inicio < 0 || fin <= inicio)
            {
                return null;
            }
            try
            {
                Dictionary<string, string> piezas = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                    respuesta.Substring(inicio, fin - inicio + 1));
                if (piezas == null)
                {
                    return null;
                }
                var porClave = new Dictionary<string, string>(piezas, StringComparer.OrdinalIgnoreCase);
                if (!porClave.TryGetValue("asunto", out string asunto) || !porClave.TryGetValue("entrada", out string entrada)
                    || !porClave.TryGetValue("cierre", out string cierre))
                {
                    return null;
                }
                return new TextoRecordatorioReposicion
                {
                    Asunto = Limpiar(asunto),
                    Entrada = Limpiar(entrada),
                    Cierre = Limpiar(cierre)
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Limpiar(string texto)
            => string.Join(" ", (texto ?? string.Empty).Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Null si el texto vale; si no, por qué no (va al log).</summary>
        internal static string MotivoNoValido(TextoRecordatorioReposicion texto)
        {
            if (texto == null || string.IsNullOrWhiteSpace(texto.Asunto) || string.IsNullOrWhiteSpace(texto.Entrada)
                || string.IsNullOrWhiteSpace(texto.Cierre))
            {
                return "falta alguna pieza";
            }
            var piezas = new[]
            {
                (Nombre: "asunto", Texto: texto.Asunto, Marca: TextoRecordatorioReposicion.MARCA_PRODUCTO, Maximo: MAXIMO_ASUNTO),
                (Nombre: "entrada", Texto: texto.Entrada, Marca: TextoRecordatorioReposicion.MARCA_PRODUCTOS, Maximo: MAXIMO_ENTRADA),
                (Nombre: "cierre", Texto: texto.Cierre, Marca: TextoRecordatorioReposicion.MARCA_VENDEDOR, Maximo: MAXIMO_CIERRE)
            };
            foreach (var pieza in piezas)
            {
                if (Veces(pieza.Texto, pieza.Marca) != 1)
                {
                    return $"el {pieza.Nombre} tiene que llevar {pieza.Marca} una vez";
                }
                string sinMarca = pieza.Texto.Replace(pieza.Marca, string.Empty);
                if (sinMarca.IndexOfAny(new[] { '{', '}', '<', '>' }) >= 0)
                {
                    return $"el {pieza.Nombre} lleva otros marcadores o HTML";
                }
                if (pieza.Texto.Length > pieza.Maximo)
                {
                    return $"el {pieza.Nombre} es demasiado largo ({pieza.Texto.Length} de {pieza.Maximo})";
                }
                Match tu = Tuteo.Match(sinMarca);
                if (tu.Success)
                {
                    return $"el {pieza.Nombre} tutea («{tu.Value}»)";
                }
                string vetada = Vetadas.FirstOrDefault(v => sinMarca.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0);
                if (vetada != null)
                {
                    return $"el {pieza.Nombre} dice «{vetada}»";
                }
            }
            return null;
        }

        private static int Veces(string texto, string marca)
        {
            int veces = 0;
            int i = 0;
            while ((i = texto.IndexOf(marca, i, StringComparison.Ordinal)) >= 0)
            {
                veces++;
                i += marca.Length;
            }
            return veces;
        }

        internal const string SYSTEM_PROMPT = @"Escribes el aviso semanal de Nueva Visión, una distribuidora de productos profesionales para centros de estética, manicura y peluquería. El aviso dice al profesional que es posible que dentro de poco se le acabe algún producto, para que no se quede sin él con un cliente delante. Es un correo de AYUDA, no de venta.

Devuelve SOLO un JSON con tres claves de texto plano (sin HTML, sin comillas raras, sin emojis):
{""asunto"": ""..."", ""entrada"": ""..."", ""cierre"": ""...""}

- asunto: una frase corta (menos de 70 caracteres) que lleve exactamente una vez el marcador {PRODUCTO}, donde irá el nombre del producto. Ejemplo: ""Que no le falte {PRODUCTO} en mitad de un servicio"".
- entrada: uno o dos frases que van después de «Hola:» y justo antes de la lista de productos (termina en dos puntos). Lleva exactamente una vez el marcador {PRODUCTOS}, que se sustituye por «este producto» o por «alguno de estos productos». Ejemplo: ""Le escribimos con un pequeño aviso: es posible que dentro de poco se le acabe {PRODUCTOS}. Preferimos avisarle con tiempo, para que no tenga que descubrirlo con un cliente ya delante:"".
- cierre: una o dos frases que van después de la lista. Dicen que si aún le queda puede olvidar el correo y que, si le hace falta, puede pedirlo desde la tienda o a {VENDEDOR}, como siempre. Lleva exactamente una vez el marcador {VENDEDOR}, que se sustituye por el nombre de su comercial o por «nuestro equipo». Ejemplo: ""Si todavía tiene de sobra, olvide este correo. Y si le hace falta, puede pedirlo desde la tienda o a {VENDEDOR}, como siempre."".

REGLAS (si no se cumplen, el texto se tira):
- Siempre DE USTED. Nunca tú, te, tu, ti, puedes, tienes, quieres, necesitas.
- Tono de ayuda, cercano y sencillo. Nada de venta: ni ofertas, ni descuentos, ni promociones, ni regalos, ni urgencias («date prisa», «últimas unidades»).
- No des a entender que miramos lo que compra: nada de «por lo que suele pedirnos», «suele», «su historial», «sus compras», «hemos visto».
- No nombres un puesto de trabajo concreto (ni camilla, ni mesa, ni tocador, ni sillón, ni cabina): tiene que valer igual para estética, manicura y peluquería. «Con un cliente delante» o «en mitad de un servicio» sí valen.
- No saludes (el «Hola:» ya lo pone el correo), no firmes y no pongas enlaces: la lista de productos, la forma de darse de baja y la firma las pone el correo.
- Español de España con todas sus tildes. Breve: la entrada no pasa de 300 caracteres ni el cierre de 240.
- Cambia la redacción cada semana; la idea es siempre la misma.";
    }
}
