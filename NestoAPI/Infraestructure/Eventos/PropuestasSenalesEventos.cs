using Hangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#615: el estado de una propuesta (EventosSenalesPropuestas.Estado).</summary>
    public static class EstadosPropuestaSenal
    {
        /// <summary>Esperando a que Administración la confirme o la descarte.</summary>
        public const string PROPUESTA = "Propuesta";
        /// <summary>Confirmada: el apunte es la señal de este evento (se marcó por el mismo camino que a mano).</summary>
        public const string CONFIRMADA = "Confirmada";
        /// <summary>Descartada a mano, o porque se confirmó otro evento para el mismo apunte. No se vuelve a proponer.</summary>
        public const string DESCARTADA = "Descartada";
    }

    /// <summary>NestoAPI#615: de dónde salió la propuesta.</summary>
    public static class OrigenesPropuestaSenal
    {
        /// <summary>Al contabilizar el cobro (api/PreContabilidades con contabilizar, p. ej. Cajas).</summary>
        public const string CONTABILIZAR = "Contabilizar";
        /// <summary>Al puntear el banco (api/Bancos/PuntearApuntes): ya se tiene el concepto de la transferencia.</summary>
        public const string CONCILIAR = "Conciliar";
        /// <summary>El barrido de cada mañana (job senales-eventos) o «Buscar» a mano.</summary>
        public const string BARRIDO = "Barrido";
    }

    /// <summary>NestoAPI#615: el interruptor de las propuestas (Web.config del REPO, como los de #612).</summary>
    public static class InterruptorPropuestasSenales
    {
        /// <summary>
        /// «true»: al contabilizar un cobro a cuenta de un cliente, al puntearlo con el banco y cada mañana se buscan las
        /// señales de evento que encajan y se proponen. Apagado: no se engancha a nada vivo (la búsqueda a mano sigue).
        /// </summary>
        public const string CLAVE = "Eventos:ProponerSenales";

        public static bool Encendido =>
            string.Equals(ConfigurationManager.AppSettings[CLAVE], "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>NestoAPI#615: una fila de EventosSenalesPropuestas (SQL directo, sin EDMX). Una por apunte y evento candidato.</summary>
    public class PropuestaSenalEvento
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int NumOrdenExtracto { get; set; }
        public int EventoId { get; set; }
        public int? InscripcionId { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Lo que el apunte tenía a favor al proponerla (positivo).</summary>
        public decimal Importe { get; set; }
        public bool PorInscripcion { get; set; }
        public bool PorConcepto { get; set; }
        /// <summary>Por qué se propone, para Administración.</summary>
        public string Detalle { get; set; }
        /// <summary>El concepto de la transferencia en el banco (si ya está punteada).</summary>
        public string ConceptoBanco { get; set; }
        public string Estado { get; set; }
        public string Origen { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#615: GET api/Eventos/Senales/Propuestas. Un apunte a favor y los eventos de los que puede ser la señal.</summary>
    public class PropuestaSenalDTO
    {
        public string Empresa { get; set; }
        public int NumOrdenExtracto { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public DateTime FechaApunte { get; set; }
        public string Documento { get; set; }
        public string Concepto { get; set; }
        public string ConceptoBanco { get; set; }
        /// <summary>Lo que el apunte tiene hoy a favor (positivo).</summary>
        public decimal Importe { get; set; }
        /// <summary>Uno, o varios si hay más de un evento posible (Administración elige).</summary>
        public List<CandidatoSenalDTO> Candidatos { get; set; } = new List<CandidatoSenalDTO>();
    }

    /// <summary>NestoAPI#615: un evento candidato. <see cref="Id"/> es el de la propuesta (para Confirmar o Descartar).</summary>
    public class CandidatoSenalDTO
    {
        public int Id { get; set; }
        public int EventoId { get; set; }
        public string Evento { get; set; }
        public DateTime FechaEvento { get; set; }
        public decimal ImporteSenal { get; set; }
        /// <summary>El cliente tiene una inscripción reservada (sin pagar) en el evento.</summary>
        public bool PorInscripcion { get; set; }
        /// <summary>El concepto del cobro menciona el evento (título o fecha).</summary>
        public bool PorConcepto { get; set; }
        /// <summary>La inscripción reservada que pasará a pagada al confirmar; null: se creará una con origen Cobro.</summary>
        public int? InscripcionId { get; set; }
        public string Motivo { get; set; }
        public string Origen { get; set; }
        public DateTime FechaPropuesta { get; set; }
    }

    /// <summary>NestoAPI#615: POST api/Eventos/Senales/Propuestas/Buscar.</summary>
    public class BuscarPropuestasSenalesDTO
    {
        /// <summary>Por defecto, la 1.</summary>
        public string Empresa { get; set; }
        /// <summary>Días hacia atrás (1 a 90). Por defecto, <see cref="ServicioPropuestasSenales.DIAS_BARRIDO"/>.</summary>
        public int? Dias { get; set; }
    }

    /// <summary>NestoAPI#615: lo que hace falta del apunte a favor para decidir (sin base de datos).</summary>
    public class ApunteParaPropuesta
    {
        public string Empresa { get; set; }
        public int NumOrden { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public DateTime Fecha { get; set; }
        public string Documento { get; set; }
        /// <summary>Lo que tiene a favor (positivo).</summary>
        public decimal ImporteAFavor { get; set; }
        /// <summary>El del extracto y los del banco.</summary>
        public List<string> Conceptos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#615: un evento del que el apunte puede ser la señal, y por qué.</summary>
    public class CandidatoSenal
    {
        public int EventoId { get; set; }
        public int? InscripcionId { get; set; }
        public bool PorInscripcion { get; set; }
        public bool PorConcepto { get; set; }
        public string Detalle { get; set; }
    }

    /// <summary>
    /// NestoAPI#615: las reglas del emparejamiento, sin base de datos. Solo PROPONE (Carlos, 08/10/26): confirma una persona.
    /// <para>Un evento es candidato si es de la empresa, está activo, su señal es exactamente lo que el apunte tiene a favor
    /// y no había pasado cuando llegó el dinero. Y además tiene que haber una prueba: que el cliente (y contacto) tenga una
    /// inscripción reservada en él, o que el concepto del cobro lo mencione (una palabra propia del título o la fecha). Con
    /// el importe solo no se propone nada: cualquier transferencia de 50 € saldría como señal de todas las masterclass.</para>
    /// </summary>
    public static class ReglasPropuestaSenal
    {
        /// <summary>Palabras de título que no distinguen un evento de otro.</summary>
        private static readonly HashSet<string> PALABRAS_GENERICAS = new HashSet<string>(StringComparer.Ordinal)
        {
            "MASTERCLASS", "MASTER", "CLASS", "CLASE", "CLASES", "FORMACION", "FORMACIONES", "CURSO", "CURSOS", "TALLER",
            "TALLERES", "EVENTO", "EVENTOS", "JORNADA", "JORNADAS", "SENAL", "SENALES", "RESERVA", "PLAZA", "PAGO", "CUENTA",
            "ANTES", "AHORA", "DESDE", "HASTA", "PARA", "CON", "SIN", "DEL", "LOS", "LAS", "UNA", "UNO", "NUEVA", "NUEVO",
            "VISION", "ONLINE", "TIENDA", "REINA", "ALCOBENDAS", "MADRID", "TRANSFERENCIA", "TRANSF"
        };

        private static readonly string[] MESES =
        {
            "ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"
        };

        /// <summary>Lo mínimo para que una palabra del título cuente.</summary>
        public const int LONGITUD_MINIMA_PALABRA = 4;
        /// <summary>Un concepto de banco recortado («MICRONEED») vale si tiene al menos estas letras.</summary>
        public const int LONGITUD_MINIMA_RECORTE = 6;

        /// <summary>Documento del cobro a cuenta del enlace de pago de una inscripción (corte 1): ya viene identificado.</summary>
        private static readonly Regex DOCUMENTO_ENLACE_INSCRIPCION = new Regex(@"^INS\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static List<CandidatoSenal> Candidatos(ApunteParaPropuesta apunte, IEnumerable<Evento> eventos,
            IEnumerable<EventoInscripcion> inscripciones)
        {
            var resultado = new List<CandidatoSenal>();
            if (apunte == null || apunte.ImporteAFavor <= 0 || EsEnlaceDeInscripcion(apunte.Documento))
            {
                return resultado;
            }
            decimal importe = Redondear(apunte.ImporteAFavor);
            string empresa = Limpiar(apunte.Empresa);
            string cliente = Limpiar(apunte.Cliente);
            string contacto = Limpiar(apunte.Contacto);
            List<EventoInscripcion> delCliente = (inscripciones ?? Enumerable.Empty<EventoInscripcion>())
                .Where(i => Limpiar(i.Cliente) == cliente && Limpiar(i.Contacto) == contacto
                    && ReglasInscripcionEvento.Estado(i.Estado) == EstadoInscripcionEvento.Reservada)
                .ToList();

            var candidatos = new List<(CandidatoSenal Candidato, DateTime Fecha)>();
            foreach (Evento evento in eventos ?? Enumerable.Empty<Evento>())
            {
                if (evento == null || !evento.Activo || Limpiar(evento.Empresa) != empresa || evento.ImporteSenal <= 0
                    || Redondear(evento.ImporteSenal) != importe || evento.Fecha.Date < apunte.Fecha.Date)
                {
                    continue;
                }
                EventoInscripcion inscripcion = delCliente.Where(i => i.EventoId == evento.Id)
                    .OrderByDescending(i => i.FechaCreacion).FirstOrDefault();
                string mencion = (apunte.Conceptos ?? new List<string>())
                    .Select(c => MencionDelEvento(c, evento)).FirstOrDefault(m => m != null);
                if (inscripcion == null && mencion == null)
                {
                    continue;
                }
                var motivos = new List<string>();
                if (inscripcion != null)
                {
                    motivos.Add($"tiene la inscripción {inscripcion.Id} reservada");
                }
                if (mencion != null)
                {
                    motivos.Add($"el concepto dice «{mencion}»");
                }
                string detalle = string.Join(" y ", motivos);
                candidatos.Add((new CandidatoSenal
                {
                    EventoId = evento.Id,
                    InscripcionId = inscripcion?.Id,
                    PorInscripcion = inscripcion != null,
                    PorConcepto = mencion != null,
                    Detalle = char.ToUpper(detalle[0], CultureInfo.GetCultureInfo("es-ES")) + detalle.Substring(1) + "."
                }, evento.Fecha));
            }
            return candidatos
                .OrderByDescending(c => c.Candidato.PorInscripcion)
                .ThenByDescending(c => c.Candidato.PorConcepto)
                .ThenBy(c => c.Fecha)
                .Select(c => c.Candidato)
                .ToList();
        }

        public static bool EsEnlaceDeInscripcion(string documento) =>
            documento != null && DOCUMENTO_ENLACE_INSCRIPCION.IsMatch(documento.Trim());

        /// <summary>
        /// Qué parte del concepto menciona el evento: una palabra propia del título (entera, recortada por el banco o en
        /// plural) o la fecha (13/10, 13-10, 13.10, «13 oct», «13 de octubre»). Null si no lo menciona.
        /// </summary>
        public static string MencionDelEvento(string concepto, Evento evento)
        {
            if (string.IsNullOrWhiteSpace(concepto) || evento == null)
            {
                return null;
            }
            string normalizado = Normalizar(concepto);
            if (normalizado.Length == 0)
            {
                return null;
            }
            List<string> palabrasConcepto = normalizado.Split(' ').Distinct().ToList();
            foreach (string palabra in PalabrasDistintivas(evento.Titulo))
            {
                string encontrada = palabrasConcepto.FirstOrDefault(p => p == palabra
                    || (p.Length >= LONGITUD_MINIMA_RECORTE && palabra.StartsWith(p, StringComparison.Ordinal))
                    || (palabra.Length >= LONGITUD_MINIMA_RECORTE && p.StartsWith(palabra, StringComparison.Ordinal) && p.Length <= palabra.Length + 2));
                if (encontrada != null)
                {
                    return encontrada;
                }
            }
            return MencionDeLaFecha(concepto, normalizado, evento.Fecha);
        }

        private static string MencionDeLaFecha(string concepto, string normalizado, DateTime fecha)
        {
            string dia = fecha.Day.ToString(CultureInfo.InvariantCulture);
            string mes = fecha.Month.ToString(CultureInfo.InvariantCulture);
            Match numerica = Regex.Match(concepto, $@"(?<!\d)0?{dia}\s*[/\-.]\s*0?{mes}(?!\d)", RegexOptions.CultureInvariant);
            if (numerica.Success)
            {
                return numerica.Value.Trim();
            }
            Match conMes = Regex.Match(normalizado, $@"(?<![0-9])0?{dia} (DE )?{MESES[fecha.Month - 1]}[A-Z]*", RegexOptions.CultureInvariant);
            return conMes.Success ? conMes.Value : null;
        }

        /// <summary>Las palabras del título que distinguen el evento (sin tildes, en mayúsculas, sin las genéricas).</summary>
        public static List<string> PalabrasDistintivas(string titulo) =>
            Normalizar(titulo).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Length >= LONGITUD_MINIMA_PALABRA && !PALABRAS_GENERICAS.Contains(p) && !p.All(char.IsDigit))
                .Distinct()
                .ToList();

        /// <summary>Sin tildes, en mayúsculas y con solo letras y cifras, separadas por un espacio.</summary>
        public static string Normalizar(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return string.Empty;
            }
            string descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            bool espacio = false;
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                if (char.IsLetterOrDigit(c))
                {
                    _ = sb.Append(char.ToUpperInvariant(c));
                    espacio = false;
                }
                else if (!espacio && sb.Length > 0)
                {
                    _ = sb.Append(' ');
                    espacio = true;
                }
            }
            return sb.ToString().Trim();
        }

        internal static decimal Redondear(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero);

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }

    /// <summary>NestoAPI#615: EventosSenalesPropuestas.</summary>
    public interface IRepositorioPropuestasSenales
    {
        /// <summary>Crea las que no existen y actualiza las que siguen en Propuesta. No toca las confirmadas ni las descartadas.</summary>
        Task Guardar(IEnumerable<PropuestaSenalEvento> propuestas);
        Task<List<PropuestaSenalEvento>> Pendientes(string empresa);
        Task<PropuestaSenalEvento> Leer(int id);
        /// <summary>Las de ese apunte que sigan en Propuesta: la del evento confirmado, Confirmada; las demás, Descartada.</summary>
        Task Resolver(string empresa, int numOrdenExtracto, int? eventoConfirmado, string usuario);
        /// <summary>False si no existe o ya no está en Propuesta.</summary>
        Task<bool> Descartar(int id, string usuario);
    }

    /// <summary>
    /// NestoAPI#615: SQL directo. Todo comprueba antes que la tabla existe (Scripts/Issue615_PropuestasSenalesEventos.sql):
    /// sin ella no se lee nada ni se guarda nada, y no falla.
    /// </summary>
    public class RepositorioPropuestasSenales : IRepositorioPropuestasSenales
    {
        private const string COLUMNAS = @"Id, RTRIM(Empresa) AS Empresa, NumOrdenExtracto, EventoId, InscripcionId, RTRIM(Cliente) AS Cliente,
       RTRIM(Contacto) AS Contacto, Importe, PorInscripcion, PorConcepto, Detalle, ConceptoBanco, Estado, Origen, Usuario, FechaCreacion, FechaModificacion";

        private const string VACIA = @"SELECT CAST(0 AS int) AS Id, CAST(NULL AS varchar(3)) AS Empresa, 0 AS NumOrdenExtracto, 0 AS EventoId,
           CAST(NULL AS int) AS InscripcionId, CAST(NULL AS varchar(10)) AS Cliente, CAST(NULL AS varchar(3)) AS Contacto,
           CAST(0 AS decimal(18,2)) AS Importe, CAST(0 AS bit) AS PorInscripcion, CAST(0 AS bit) AS PorConcepto,
           CAST(NULL AS nvarchar(300)) AS Detalle, CAST(NULL AS nvarchar(500)) AS ConceptoBanco, CAST(NULL AS varchar(15)) AS Estado,
           CAST(NULL AS varchar(15)) AS Origen, CAST(NULL AS varchar(50)) AS Usuario, GETDATE() AS FechaCreacion, GETDATE() AS FechaModificacion
    WHERE 1 = 0";

        internal const string SQL_PENDIENTES = @"
IF OBJECT_ID('dbo.EventosSenalesPropuestas') IS NOT NULL
    EXEC sp_executesql N'SELECT " + COLUMNAS + @" FROM dbo.EventosSenalesPropuestas WHERE Empresa = @e AND Estado = ''Propuesta''',
        N'@e char(3)', @e = @p0
ELSE
    " + VACIA;

        internal const string SQL_LEER = @"
IF OBJECT_ID('dbo.EventosSenalesPropuestas') IS NOT NULL
    EXEC sp_executesql N'SELECT " + COLUMNAS + @" FROM dbo.EventosSenalesPropuestas WHERE Id = @id', N'@id int', @id = @p0
ELSE
    " + VACIA;

        // El INSERT choca con el UNIQUE si otro proceso la crea a la vez (se ignora: la propuesta ya está).
        internal const string SQL_GUARDAR = @"
SET NOCOUNT ON;
IF OBJECT_ID('dbo.EventosSenalesPropuestas') IS NOT NULL
BEGIN
    UPDATE dbo.EventosSenalesPropuestas
    SET InscripcionId = @p3, Importe = @p6, PorInscripcion = @p7, PorConcepto = @p8, Detalle = @p9,
        ConceptoBanco = ISNULL(@p10, ConceptoBanco), FechaModificacion = GETDATE()
    WHERE Empresa = @p0 AND NumOrdenExtracto = @p1 AND EventoId = @p2 AND Estado = 'Propuesta';
    IF NOT EXISTS (SELECT 1 FROM dbo.EventosSenalesPropuestas WHERE Empresa = @p0 AND NumOrdenExtracto = @p1 AND EventoId = @p2)
    BEGIN TRY
        INSERT INTO dbo.EventosSenalesPropuestas (Empresa, NumOrdenExtracto, EventoId, InscripcionId, Cliente, Contacto, Importe,
            PorInscripcion, PorConcepto, Detalle, ConceptoBanco, Estado, Origen, Usuario, FechaCreacion, FechaModificacion)
        VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, 'Propuesta', @p11, @p12, GETDATE(), GETDATE());
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
    END CATCH
END";

        internal const string SQL_RESOLVER = @"
IF OBJECT_ID('dbo.EventosSenalesPropuestas') IS NOT NULL
    UPDATE dbo.EventosSenalesPropuestas
    SET Estado = CASE WHEN EventoId = @p2 THEN 'Confirmada' ELSE 'Descartada' END, Usuario = @p3, FechaModificacion = GETDATE()
    WHERE Empresa = @p0 AND NumOrdenExtracto = @p1 AND Estado = 'Propuesta'";

        internal const string SQL_DESCARTAR = @"
SET NOCOUNT ON;
IF OBJECT_ID('dbo.EventosSenalesPropuestas') IS NOT NULL
    UPDATE dbo.EventosSenalesPropuestas SET Estado = 'Descartada', Usuario = @p1, FechaModificacion = GETDATE()
    WHERE Id = @p0 AND Estado = 'Propuesta';
SELECT @@ROWCOUNT;";

        private readonly NVEntities db;

        public RepositorioPropuestasSenales(NVEntities db)
        {
            this.db = db;
        }

        public async Task Guardar(IEnumerable<PropuestaSenalEvento> propuestas)
        {
            foreach (PropuestaSenalEvento p in propuestas ?? Enumerable.Empty<PropuestaSenalEvento>())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                    P("@p0", p.Empresa), P("@p1", p.NumOrdenExtracto), P("@p2", p.EventoId), P("@p3", p.InscripcionId),
                    P("@p4", p.Cliente), P("@p5", p.Contacto), P("@p6", p.Importe), P("@p7", p.PorInscripcion),
                    P("@p8", p.PorConcepto), P("@p9", Recortar(p.Detalle, 300)), P("@p10", Recortar(p.ConceptoBanco, 500)),
                    P("@p11", p.Origen), P("@p12", Recortar(p.Usuario, 50))).ConfigureAwait(false);
            }
        }

        public async Task<List<PropuestaSenalEvento>> Pendientes(string empresa) =>
            await db.Database.SqlQuery<PropuestaSenalEvento>(SQL_PENDIENTES, P("@p0", empresa)).ToListAsync().ConfigureAwait(false);

        public async Task<PropuestaSenalEvento> Leer(int id) =>
            (await db.Database.SqlQuery<PropuestaSenalEvento>(SQL_LEER, P("@p0", id)).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task Resolver(string empresa, int numOrdenExtracto, int? eventoConfirmado, string usuario) =>
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_RESOLVER, P("@p0", empresa), P("@p1", numOrdenExtracto),
                P("@p2", eventoConfirmado ?? 0), P("@p3", Recortar(usuario, 50))).ConfigureAwait(false);

        public async Task<bool> Descartar(int id, string usuario)
        {
            List<int> filas = await db.Database.SqlQuery<int>(SQL_DESCARTAR, P("@p0", id), P("@p1", Recortar(usuario, 50)))
                .ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault() > 0;
        }

        private static SqlParameter P(string nombre, object valor) => new SqlParameter(nombre, valor ?? DBNull.Value);

        private static string Recortar(string texto, int max) =>
            texto == null ? null : texto.Length > max ? texto.Substring(0, max) : texto;
    }

    /// <summary>NestoAPI#615: el concepto de la transferencia en el banco, de los apuntes ya punteados de cada asiento.</summary>
    public interface ILectorConceptosBanco
    {
        /// <summary>asiento → textos del banco (registros de concepto y referencias). Sin punteo, el asiento no sale.</summary>
        Task<Dictionary<int, List<string>>> Leer(string empresa, IEnumerable<int> asientos);
    }

    /// <summary>Contabilidad (mismo asiento) → ConciliacionBancariaPunteo → ApunteBancario y sus RegistroComplementarioConcepto.</summary>
    public class LectorConceptosBanco : ILectorConceptosBanco
    {
        private readonly NVEntities db;

        public LectorConceptosBanco(NVEntities db)
        {
            this.db = db;
        }

        public async Task<Dictionary<int, List<string>>> Leer(string empresa, IEnumerable<int> asientos)
        {
            var resultado = new Dictionary<int, List<string>>();
            List<int> lista = (asientos ?? Enumerable.Empty<int>()).Where(a => a > 0).Distinct().ToList();
            if (!lista.Any())
            {
                return resultado;
            }
            var lineas = await db.Contabilidades
                .Where(c => c.Empresa == empresa && c.Asiento != null && lista.Contains(c.Asiento.Value))
                .Select(c => new { c.Nº_Orden, c.Asiento })
                .ToListAsync().ConfigureAwait(false);
            List<int> ordenes = lineas.Select(l => l.Nº_Orden).Distinct().ToList();
            if (!ordenes.Any())
            {
                return resultado;
            }
            var punteos = await db.ConciliacionesBancariasPunteos
                .Where(p => p.ApunteContabilidadId != null && p.ApunteBancoId != null && ordenes.Contains(p.ApunteContabilidadId.Value))
                .Select(p => new { p.ApunteContabilidadId, p.ApunteBancoId })
                .ToListAsync().ConfigureAwait(false);
            List<int> bancos = punteos.Select(p => p.ApunteBancoId.Value).Distinct().ToList();
            if (!bancos.Any())
            {
                return resultado;
            }
            var apuntes = await db.ApuntesBancarios.Where(a => bancos.Contains(a.Id))
                .Select(a => new { a.Id, a.Referencia1, a.Referencia2 })
                .ToListAsync().ConfigureAwait(false);
            var conceptos = await db.RegistrosComplementariosConceptos.Where(r => bancos.Contains(r.ApuntesBancariosId))
                .OrderBy(r => r.Id)
                .Select(r => new { r.ApuntesBancariosId, r.Concepto, r.Concepto2 })
                .ToListAsync().ConfigureAwait(false);

            Dictionary<int, List<string>> textosBanco = bancos.ToDictionary(b => b, b =>
                conceptos.Where(c => c.ApuntesBancariosId == b).SelectMany(c => new[] { c.Concepto, c.Concepto2 })
                    .Concat(apuntes.Where(a => a.Id == b).SelectMany(a => new[] { a.Referencia1, a.Referencia2 }))
                    .Select(t => t?.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList());
            foreach (var linea in lineas)
            {
                List<string> textos = punteos.Where(p => p.ApunteContabilidadId == linea.Nº_Orden)
                    .SelectMany(p => textosBanco[p.ApunteBancoId.Value]).ToList();
                if (!textos.Any())
                {
                    continue;
                }
                int asiento = linea.Asiento.Value;
                if (!resultado.TryGetValue(asiento, out List<string> delAsiento))
                {
                    delAsiento = new List<string>();
                    resultado[asiento] = delAsiento;
                }
                delAsiento.AddRange(textos.Where(t => !delAsiento.Contains(t)));
            }
            return resultado;
        }
    }

    public interface IServicioPropuestasSenales
    {
        /// <summary>Los cobros a cuenta de clientes de ese asiento (tras contabilizar). Devuelve cuántas propuestas ha guardado.</summary>
        Task<int> ProponerParaAsiento(string empresa, int asiento, string origen);
        /// <summary>El asiento de esa línea de contabilidad (tras puntearla con el banco).</summary>
        Task<int> ProponerParaApunteContabilidad(int numOrdenContabilidad, string origen);
        /// <summary>Los cobros a cuenta desde esa fecha cuyo importe es la señal de algún evento (barrido).</summary>
        Task<int> ProponerRecientes(string empresa, DateTime desde);
        Task<List<PropuestaSenalDTO>> LeerPendientes(string empresa);
        Task<PropuestaSenalEvento> LeerPropuesta(int id);
        Task Resolver(string empresa, int numOrdenExtracto, int? eventoConfirmado, string usuario);
        Task<bool> Descartar(int id, string usuario);
    }

    /// <summary>
    /// NestoAPI#615 (corte 5 de #610): propone la señal del evento al contabilizar el cobro, al puntearlo con el banco y en
    /// el barrido de cada mañana. Las tres entradas solo eligen los apuntes; el resto es común (<see cref="Proponer"/> con
    /// <see cref="ReglasPropuestaSenal"/>). No marca nada: confirmar es marcar la señal por el mismo camino que a mano
    /// (POST api/Eventos/{id}/Senales y, con el corte 2 encendido, la factura del curso y el cupón).
    /// </summary>
    public class ServicioPropuestasSenales : IServicioPropuestasSenales
    {
        /// <summary>Días hacia atrás que mira el barrido.</summary>
        public const int DIAS_BARRIDO = 15;

        private readonly NVEntities db;
        private readonly IRepositorioPropuestasSenales repositorio;
        private readonly ILectorConceptosBanco lectorBanco;
        private readonly Func<DateTime> reloj;
        private readonly string usuario;

        public ServicioPropuestasSenales(NVEntities db)
            : this(db, new RepositorioPropuestasSenales(db), new LectorConceptosBanco(db), () => DateTime.Now, InterruptoresEventos.UsuarioAutomatico)
        {
        }

        internal ServicioPropuestasSenales(NVEntities db, IRepositorioPropuestasSenales repositorio, ILectorConceptosBanco lectorBanco,
            Func<DateTime> reloj, string usuario)
        {
            this.db = db;
            this.repositorio = repositorio;
            this.lectorBanco = lectorBanco;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.usuario = string.IsNullOrWhiteSpace(usuario) ? InterruptoresEventos.USUARIO_AUTOMATICO : usuario.Trim();
        }

        public async Task<int> ProponerParaAsiento(string empresa, int asiento, string origen)
        {
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            List<ExtractoCliente> apuntes = await db.ExtractosCliente
                .Where(e => e.Empresa == empresaLimpia && e.Asiento == asiento && e.ImportePdte < 0
                    && e.TipoApunte == Constantes.TiposExtractoCliente.PAGO)
                .ToListAsync().ConfigureAwait(false);
            return await Proponer(empresaLimpia, apuntes, origen).ConfigureAwait(false);
        }

        public async Task<int> ProponerParaApunteContabilidad(int numOrdenContabilidad, string origen)
        {
            var linea = await db.Contabilidades.Where(c => c.Nº_Orden == numOrdenContabilidad)
                .Select(c => new { c.Empresa, c.Asiento })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (linea?.Asiento == null)
            {
                return 0;
            }
            return await ProponerParaAsiento(linea.Empresa, linea.Asiento.Value, origen).ConfigureAwait(false);
        }

        public async Task<int> ProponerRecientes(string empresa, DateTime desde)
        {
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            DateTime hoy = reloj().Date;
            DateTime fechaDesde = desde.Date;
            // Calculada fuera de la consulta: LINQ to Entities no traduce DateTime.AddDays (fallaba cada mañana en el job de señales).
            DateTime manana = hoy.AddDays(1);
            // Solo importes que son la señal de algún evento activo que no ha pasado: sin eso no hay nada que buscar.
            List<decimal> importes = (await db.Eventos
                .Where(e => e.Empresa == empresaLimpia && e.Activo && e.ImporteSenal > 0 && e.Fecha >= fechaDesde)
                .Select(e => e.ImporteSenal).ToListAsync().ConfigureAwait(false))
                .Select(ReglasPropuestaSenal.Redondear).Distinct().ToList();
            if (!importes.Any())
            {
                return 0;
            }
            List<decimal> aFavor = importes.Select(i => -i).ToList();
            List<ExtractoCliente> apuntes = await db.ExtractosCliente
                .Where(e => e.Empresa == empresaLimpia && e.Fecha >= fechaDesde && e.Fecha <= manana
                    && e.TipoApunte == Constantes.TiposExtractoCliente.PAGO && aFavor.Contains(e.ImportePdte))
                .ToListAsync().ConfigureAwait(false);
            return await Proponer(empresaLimpia, apuntes, OrigenesPropuestaSenal.BARRIDO).ConfigureAwait(false);
        }

        /// <summary>El núcleo común: de los apuntes a favor que aún no son señal, guarda los eventos de los que pueden serlo.</summary>
        internal async Task<int> Proponer(string empresa, List<ExtractoCliente> apuntes, string origen)
        {
            List<ExtractoCliente> aFavor = (apuntes ?? new List<ExtractoCliente>())
                .Where(a => a.ImportePdte < 0 && a.TipoApunte?.Trim() == Constantes.TiposExtractoCliente.PAGO
                    && !ReglasPropuestaSenal.EsEnlaceDeInscripcion(a.Nº_Documento))
                .GroupBy(a => a.Nº_Orden).Select(g => g.First())
                .ToList();
            if (!aFavor.Any())
            {
                return 0;
            }

            List<int> ordenes = aFavor.Select(a => a.Nº_Orden).ToList();
            HashSet<int> yaSonSenal = new HashSet<int>(await db.EventosSenales
                .Where(s => s.Empresa == empresa && ordenes.Contains(s.NumOrdenExtracto))
                .Select(s => s.NumOrdenExtracto).ToListAsync().ConfigureAwait(false));
            aFavor = aFavor.Where(a => !yaSonSenal.Contains(a.Nº_Orden)).ToList();
            if (!aFavor.Any())
            {
                return 0;
            }

            List<decimal> importes = aFavor.Select(a => ReglasPropuestaSenal.Redondear(-a.ImportePdte)).Distinct().ToList();
            DateTime desde = aFavor.Min(a => a.Fecha).Date;
            List<Evento> eventos = await db.Eventos
                .Where(e => e.Empresa == empresa && e.Activo && importes.Contains(e.ImporteSenal) && e.Fecha >= desde)
                .ToListAsync().ConfigureAwait(false);
            if (!eventos.Any())
            {
                return 0;
            }

            List<int> idsEventos = eventos.Select(e => e.Id).ToList();
            List<string> clientes = aFavor.Select(a => a.Número?.Trim()).Distinct().ToList();
            string reservada = EstadoInscripcionEvento.Reservada.ToString();
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => idsEventos.Contains(i.EventoId) && clientes.Contains(i.Cliente) && i.Estado == reservada)
                .ToListAsync().ConfigureAwait(false);
            Dictionary<int, List<string>> conceptosBanco = await lectorBanco
                .Leer(empresa, aFavor.Select(a => a.Asiento)).ConfigureAwait(false) ?? new Dictionary<int, List<string>>();

            var propuestas = new List<PropuestaSenalEvento>();
            foreach (ExtractoCliente apunte in aFavor)
            {
                conceptosBanco.TryGetValue(apunte.Asiento, out List<string> delBanco);
                var paraReglas = new ApunteParaPropuesta
                {
                    Empresa = empresa,
                    NumOrden = apunte.Nº_Orden,
                    Cliente = apunte.Número,
                    Contacto = apunte.Contacto,
                    Fecha = apunte.Fecha,
                    Documento = apunte.Nº_Documento,
                    ImporteAFavor = -apunte.ImportePdte,
                    Conceptos = new[] { apunte.Concepto }.Concat(delBanco ?? new List<string>())
                        .Where(c => !string.IsNullOrWhiteSpace(c)).ToList()
                };
                string conceptoBanco = delBanco != null && delBanco.Any() ? string.Join(" · ", delBanco) : null;
                foreach (CandidatoSenal candidato in ReglasPropuestaSenal.Candidatos(paraReglas, eventos, inscripciones))
                {
                    propuestas.Add(new PropuestaSenalEvento
                    {
                        Empresa = empresa,
                        NumOrdenExtracto = apunte.Nº_Orden,
                        EventoId = candidato.EventoId,
                        InscripcionId = candidato.InscripcionId,
                        Cliente = apunte.Número?.Trim(),
                        Contacto = apunte.Contacto?.Trim(),
                        Importe = ReglasPropuestaSenal.Redondear(-apunte.ImportePdte),
                        PorInscripcion = candidato.PorInscripcion,
                        PorConcepto = candidato.PorConcepto,
                        Detalle = candidato.Detalle,
                        ConceptoBanco = conceptoBanco,
                        Origen = origen,
                        Usuario = usuario
                    });
                }
            }
            if (propuestas.Any())
            {
                await repositorio.Guardar(propuestas).ConfigureAwait(false);
            }
            return propuestas.Count;
        }

        /// <summary>
        /// Las propuestas sin confirmar, agrupadas por apunte. No salen las de un apunte que ya es señal (marcado a mano) o que
        /// ya no tiene nada a favor (se liquidó), ni las de eventos borrados o desactivados.
        /// </summary>
        public async Task<List<PropuestaSenalDTO>> LeerPendientes(string empresa)
        {
            string empresaLimpia = EmpresaOPorDefecto(empresa);
            List<PropuestaSenalEvento> filas = await repositorio.Pendientes(empresaLimpia).ConfigureAwait(false) ?? new List<PropuestaSenalEvento>();
            if (!filas.Any())
            {
                return new List<PropuestaSenalDTO>();
            }
            List<int> ordenes = filas.Select(f => f.NumOrdenExtracto).Distinct().ToList();
            List<int> idsEventos = filas.Select(f => f.EventoId).Distinct().ToList();
            Dictionary<int, ExtractoCliente> apuntes = (await db.ExtractosCliente
                .Where(e => e.Empresa == empresaLimpia && ordenes.Contains(e.Nº_Orden)).ToListAsync().ConfigureAwait(false))
                .GroupBy(e => e.Nº_Orden).ToDictionary(g => g.Key, g => g.First());
            HashSet<int> yaSonSenal = new HashSet<int>(await db.EventosSenales
                .Where(s => s.Empresa == empresaLimpia && ordenes.Contains(s.NumOrdenExtracto))
                .Select(s => s.NumOrdenExtracto).ToListAsync().ConfigureAwait(false));
            Dictionary<int, Evento> eventos = (await db.Eventos.Where(e => idsEventos.Contains(e.Id)).ToListAsync().ConfigureAwait(false))
                .ToDictionary(e => e.Id);
            List<string> clientes = filas.Select(f => f.Cliente?.Trim()).Distinct().ToList();
            Dictionary<string, string> nombres = (await db.Clientes
                .Where(c => c.Empresa == empresaLimpia && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Nombre })
                .ToListAsync().ConfigureAwait(false))
                .GroupBy(c => c.Nº_Cliente?.Trim() + "/" + c.Contacto?.Trim())
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());

            var resultado = new List<PropuestaSenalDTO>();
            foreach (IGrouping<int, PropuestaSenalEvento> grupo in filas.GroupBy(f => f.NumOrdenExtracto))
            {
                if (yaSonSenal.Contains(grupo.Key) || !apuntes.TryGetValue(grupo.Key, out ExtractoCliente apunte) || apunte.ImportePdte >= 0)
                {
                    continue;
                }
                List<CandidatoSenalDTO> candidatos = grupo
                    .Where(f => eventos.TryGetValue(f.EventoId, out Evento ev) && ev.Activo)
                    .Select(f =>
                    {
                        Evento evento = eventos[f.EventoId];
                        return new CandidatoSenalDTO
                        {
                            Id = f.Id,
                            EventoId = f.EventoId,
                            Evento = evento.Titulo?.Trim(),
                            FechaEvento = evento.Fecha.Date,
                            ImporteSenal = evento.ImporteSenal,
                            PorInscripcion = f.PorInscripcion,
                            PorConcepto = f.PorConcepto,
                            InscripcionId = f.InscripcionId,
                            Motivo = f.Detalle,
                            Origen = f.Origen?.Trim(),
                            FechaPropuesta = f.FechaCreacion
                        };
                    })
                    .OrderByDescending(c => c.PorInscripcion).ThenByDescending(c => c.PorConcepto).ThenBy(c => c.FechaEvento)
                    .ToList();
                if (!candidatos.Any())
                {
                    continue;
                }
                PropuestaSenalEvento primera = grupo.First();
                string cliente = apunte.Número?.Trim();
                string contacto = apunte.Contacto?.Trim();
                nombres.TryGetValue(cliente + "/" + contacto, out string nombre);
                resultado.Add(new PropuestaSenalDTO
                {
                    Empresa = empresaLimpia,
                    NumOrdenExtracto = grupo.Key,
                    Cliente = cliente,
                    Contacto = contacto,
                    Nombre = nombre,
                    FechaApunte = apunte.Fecha,
                    Documento = apunte.Nº_Documento?.Trim(),
                    Concepto = apunte.Concepto?.Trim(),
                    ConceptoBanco = grupo.Select(f => f.ConceptoBanco).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
                    Importe = -apunte.ImportePdte,
                    Candidatos = candidatos
                });
            }
            return resultado.OrderBy(p => p.FechaApunte).ThenBy(p => p.NumOrdenExtracto).ToList();
        }

        public Task<PropuestaSenalEvento> LeerPropuesta(int id) => repositorio.Leer(id);

        public Task Resolver(string empresa, int numOrdenExtracto, int? eventoConfirmado, string usuarioResuelve) =>
            repositorio.Resolver(EmpresaOPorDefecto(empresa), numOrdenExtracto, eventoConfirmado, UsuarioAuditoriaHelper.ParaAuditoria(usuarioResuelve));

        public Task<bool> Descartar(int id, string usuarioDescarta) =>
            repositorio.Descartar(id, UsuarioAuditoriaHelper.ParaAuditoria(usuarioDescarta));

        private static string EmpresaOPorDefecto(string empresa)
        {
            string limpia = empresa?.Trim();
            return string.IsNullOrEmpty(limpia) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : limpia;
        }
    }

    /// <summary>
    /// NestoAPI#615: el enganche con lo vivo (contabilizar y puntear). Con el interruptor apagado no hace nada; encendido, solo
    /// encola el trabajo en Hangfire (el usuario no espera y un fallo nunca deshace ni frena la contabilización). Nunca lanza.
    /// </summary>
    public class EncoladorPropuestasSenales
    {
        private readonly Func<bool> interruptor;
        private readonly Action<string, int> encolarAsiento;
        private readonly Action<int> encolarApunteContabilidad;

        public EncoladorPropuestasSenales()
            : this(() => InterruptorPropuestasSenales.Encendido,
                  (empresa, asiento) => BackgroundJob.Enqueue(() => PropuestasSenalesJobsService.TrasContabilizar(empresa, asiento)),
                  numOrden => BackgroundJob.Enqueue(() => PropuestasSenalesJobsService.TrasPuntear(numOrden)))
        {
        }

        internal EncoladorPropuestasSenales(Func<bool> interruptor, Action<string, int> encolarAsiento, Action<int> encolarApunteContabilidad)
        {
            this.interruptor = interruptor ?? (() => false);
            this.encolarAsiento = encolarAsiento;
            this.encolarApunteContabilidad = encolarApunteContabilidad;
        }

        /// <summary>Tras contabilizar: si hay algún cobro a cuenta de cliente (pago al haber que no liquida nada), mira el asiento.</summary>
        public void TrasContabilizar(IEnumerable<PreContabilidad> lineas, int asiento)
        {
            try
            {
                if (asiento <= 0 || !interruptor())
                {
                    return;
                }
                foreach (string empresa in EmpresasConCobroACuenta(lineas))
                {
                    encolarAsiento(empresa, asiento);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Señales #615] No se han podido buscar señales tras contabilizar el asiento {asiento}: {ex.Message}", ex));
            }
        }

        /// <summary>Tras puntear un apunte del banco con uno de contabilidad: ya se sabe el concepto de la transferencia.</summary>
        public void TrasPuntear(int? apunteBancoId, int? apunteContabilidadId)
        {
            try
            {
                if (apunteBancoId == null || apunteContabilidadId == null || !interruptor())
                {
                    return;
                }
                encolarApunteContabilidad(apunteContabilidadId.Value);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Señales #615] No se han podido buscar señales tras puntear el apunte {apunteContabilidadId}: {ex.Message}", ex));
            }
        }

        internal static List<string> EmpresasConCobroACuenta(IEnumerable<PreContabilidad> lineas) =>
            (lineas ?? Enumerable.Empty<PreContabilidad>())
                .Where(l => l != null && l.TipoCuenta?.Trim() == Constantes.Contabilidad.TiposCuenta.CLIENTE
                    && l.TipoApunte?.Trim() == Constantes.TiposExtractoCliente.PAGO
                    && l.Haber > l.Debe && (l.Liquidado == null || l.Liquidado == 0))
                .Select(l => l.Empresa?.Trim())
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct()
                .ToList();
    }

    /// <summary>NestoAPI#615: los trabajos que encola <see cref="EncoladorPropuestasSenales"/>.</summary>
    public static class PropuestasSenalesJobsService
    {
        [AutomaticRetry(Attempts = 2)]
        public static Task TrasContabilizar(string empresa, int asiento) =>
            Ejecutar(s => s.ProponerParaAsiento(empresa, asiento, OrigenesPropuestaSenal.CONTABILIZAR), $"asiento {asiento} de la empresa {empresa}");

        [AutomaticRetry(Attempts = 2)]
        public static Task TrasPuntear(int apunteContabilidadId) =>
            Ejecutar(s => s.ProponerParaApunteContabilidad(apunteContabilidadId, OrigenesPropuestaSenal.CONCILIAR), $"apunte de contabilidad {apunteContabilidadId}");

        private static async Task Ejecutar(Func<ServicioPropuestasSenales, Task<int>> trabajo, string que)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await trabajo(new ServicioPropuestasSenales(db)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Señales #615] Error al buscar señales ({que}): {ex.Message}", ex));
                throw;
            }
        }
    }
}
