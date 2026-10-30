using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>NestoAPI#751: una sugerencia enseñada, con quién la vio y lo que contestó. Lo que lee <see cref="ResumenRespuestasSugerencias.SQL_FILAS"/>.</summary>
    public class FilaRespuestaSugerencia
    {
        /// <summary>El usuario que tenía abierta la plantilla (Nesto lo manda con dominio, NestoApp sin él).</summary>
        public string Usuario { get; set; }
        public DateTime FechaMostrada { get; set; }
        public string Respuesta { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        /// <summary>Solo el principio del Texto (lo justo para ver las marcas provisionales de «Quitar» y «Sustituida»).</summary>
        public string Texto { get; set; }
        public bool? EntroEnPedido { get; set; }
        /// <summary>Base imponible del producto en el pedido guardado, solo si se contestó ✓ y entró en el pedido.</summary>
        public decimal? ImporteEnPedido { get; set; }
    }

    /// <summary>NestoAPI#751: lo de una semana (de lunes a domingo) de un usuario.</summary>
    public class ResumenSemanaSugerenciasDTO
    {
        /// <summary>El lunes.</summary>
        public DateTime Desde { get; set; }
        /// <summary>Exclusivo: el lunes siguiente.</summary>
        public DateTime Hasta { get; set; }
        public int Mostradas { get; set; }
        /// <summary>✓ + ✗ + 🚫 (lo que ha marcado el vendedor; «Sustituida» no es de nadie).</summary>
        public int Respuestas { get; set; }
        public int Anadidas { get; set; }
        public int NoLeInteresa { get; set; }
        public int Quitadas { get; set; }
        /// <summary>✓ que acabaron en el pedido guardado.</summary>
        public int AnadidasEnPedido { get; set; }
        /// <summary>Base imponible de esas ✓ en sus pedidos.</summary>
        public decimal ImporteVentas { get; set; }
    }

    /// <summary>
    /// NestoAPI#751: el resumen personal que ve el vendedor en la cinta «Puede ofrecer» (GET api/SugerenciasProductos/MiResumen).
    /// Solo lo suyo: nunca se compara con nadie. Los textos los monta la API para que Nesto y NestoApp digan lo mismo.
    /// </summary>
    public class ResumenSugerenciasVendedorDTO
    {
        /// <summary>Las últimas semanas, de la más antigua a la actual (la última va a medias).</summary>
        public List<ResumenSemanaSugerenciasDTO> Semanas { get; set; } = new List<ResumenSemanaSugerenciasDTO>();
        public ResumenSemanaSugerenciasDTO EstaSemana { get; set; }
        /// <summary>
        /// De cada 10 que ha ofrecido (✓ + ✗) en estas semanas, cuántas le han aceptado. Null con menos de
        /// <see cref="ResumenRespuestasSugerencias.MINIMO_OFRECIDAS_PARA_ACIERTO"/> ofrecidas (con tan pocas no dice nada).
        /// </summary>
        public int? AceptadasDeCadaDiez { get; set; }
        /// <summary>Una línea para la cabecera de la cinta.</summary>
        public string Frase { get; set; }
        /// <summary>El detalle (la evolución por semanas y para qué sirve marcar), para la ayuda de la cabecera.</summary>
        public string Ayuda { get; set; }
    }

    /// <summary>NestoAPI#751 y #109: la participación de un usuario con las sugerencias en un periodo, con sus puntos.</summary>
    public class ParticipacionSugerenciasUsuarioDTO
    {
        /// <summary>Sin dominio: el mismo usuario en Nesto y en NestoApp suma junto.</summary>
        public string Usuario { get; set; }
        public int Anadidas { get; set; }
        public int NoLeInteresa { get; set; }
        public int Quitadas { get; set; }
        public int AnadidasEnPedido { get; set; }
        public decimal ImporteVentas { get; set; }
        /// <summary>Respuestas que no suman por pasar del tope diario.</summary>
        public int RespuestasSinPuntos { get; set; }
        public int Puntos { get; set; }
    }

    public class ParticipacionSugerenciasDTO
    {
        public DateTime Desde { get; set; }
        /// <summary>Exclusivo.</summary>
        public DateTime Hasta { get; set; }
        /// <summary>De más a menos puntos.</summary>
        public List<ParticipacionSugerenciasUsuarioDTO> PorUsuario { get; set; } = new List<ParticipacionSugerenciasUsuarioDTO>();
        /// <summary>Cómo se cuentan los puntos (para que quien prepare las Novedades no tenga que buscarlo).</summary>
        public string Reglas { get; set; }
    }

    /// <summary>
    /// NestoAPI#751: que al vendedor le compense pulsar ✓/✗ en «Puede ofrecer». Dos cosas, las dos CALCULADAS con lo que ya se
    /// guarda (SugerenciasProductosMostradas.Respuesta y EntroEnPedido, y la línea del pedido): ni DDL ni escrituras nuevas.
    /// <list type="number">
    /// <item>Que vea lo que gana: <see cref="CalcularResumen"/> (su semana, la evolución y, de cada 10 que ofrece, cuántas le
    /// aceptan). Se enseña en la cabecera de la cinta, solo a él.</item>
    /// <item>Puntos para los key users (#109): <see cref="CalcularParticipacion"/>. Como decidió Carlos el 23/10/26 (#109),
    /// todavía NO se enseñan a los vendedores: los ve Dirección/Informática para el agradecimiento público de las Novedades.</item>
    /// </list>
    /// <para>Calidad de los datos (Carlos, 30/10/26): un producto que entra en el pedido sin pulsar ✓ NO cuenta como «sí» ni da
    /// puntos; solo cuenta lo que marca el vendedor.</para>
    /// </summary>
    public static class ResumenRespuestasSugerencias
    {
        public const int SEMANAS_RESUMEN = 4;
        public const int DIAS_PARTICIPACION_POR_DEFECTO = 30;
        public const int MINIMO_OFRECIDAS_PARA_ACIERTO = 5;

        // Puntos (modelo Stack Overflow, #109). Provisionales: los decide Carlos (ver el comentario de la issue #751).
        public const int PUNTOS_ANADIDO = 5;
        public const int PUNTOS_NO_LE_INTERESA = 5;
        public const int PUNTOS_QUITADA = 2;
        /// <summary>Un ✓ que termina en venta (el producto está en el pedido guardado) vale más.</summary>
        public const int PUNTOS_EXTRA_ANADIDO_EN_PEDIDO = 5;
        /// <summary>Tope diario (#109): a partir de aquí las respuestas del día no suman, para que no se pulse por pulsar.</summary>
        public const int MAXIMO_RESPUESTAS_CON_PUNTOS_AL_DIA = 20;

        public const string FRASE_ANIMO = "Cuanto más marque ✓ o ✗, más acierta con sus clientes.";

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        // Pocas filas (unos miles al mes en toda la empresa). La línea del pedido solo se busca para las ✓ que entraron.
        // @p2 = usuario sin dominio, o NULL para todos.
        internal const string SQL_FILAS = @"
SELECT CASE WHEN CHARINDEX('\', s.Usuario) > 0 THEN SUBSTRING(s.Usuario, CHARINDEX('\', s.Usuario) + 1, 50) ELSE s.Usuario END AS Usuario,
       m.FechaMostrada, m.Respuesta, m.FechaRespuesta, LEFT(m.Texto, 30) AS Texto, m.EntroEnPedido,
       CASE WHEN m.Respuesta = 'Anadido' AND m.EntroEnPedido = 1 AND s.Pedido IS NOT NULL THEN
           (SELECT SUM(l.[Base Imponible]) FROM dbo.LinPedidoVta l WITH (NOLOCK)
            WHERE l.Empresa = s.Empresa AND l.[Número] = s.Pedido AND l.Producto = m.Producto)
       END AS ImporteEnPedido
FROM dbo.SugerenciasProductosMostradas m WITH (NOLOCK)
JOIN dbo.SugerenciasProductosSesiones s WITH (NOLOCK) ON s.Id = m.SesionId
WHERE m.FechaMostrada >= @p0 AND m.FechaMostrada < @p1
  AND (@p2 IS NULL OR s.Usuario = @p2 OR s.Usuario LIKE '%\' + @p2)";

        /// <summary>Sin las tablas del script Issue670, lista vacía. <paramref name="usuario"/> null: todos.</summary>
        public static async Task<List<FilaRespuestaSugerencia>> Leer(NVEntities db, DateTime desde, DateTime hasta, string usuario)
        {
            bool existen = await db.Database.SqlQuery<bool>(SugerenciasProductosSql.SQL_EXISTEN_TABLAS).FirstAsync().ConfigureAwait(false);
            if (!existen)
            {
                return new List<FilaRespuestaSugerencia>();
            }
            return await db.Database.SqlQuery<FilaRespuestaSugerencia>(SQL_FILAS,
                SugerenciasProductosSql.Fecha("@p0", desde), SugerenciasProductosSql.Fecha("@p1", hasta),
                SugerenciasProductosSql.Nvarchar("@p2", usuario == null ? null : SinDominio(usuario), 50)).ToListAsync().ConfigureAwait(false);
        }

        /// <summary>«NUEVAVISION\Paloma» → «Paloma» (NestoApp ya lo manda sin dominio).</summary>
        public static string SinDominio(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return usuario;
            }
            string limpio = usuario.Trim();
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        /// <summary>El lunes de la semana de <paramref name="dia"/>.</summary>
        public static DateTime Lunes(DateTime dia)
        {
            int desdeLunes = ((int)dia.DayOfWeek + 6) % 7;
            return dia.Date.AddDays(-desdeLunes);
        }

        /// <summary>Desde el lunes de hace <see cref="SEMANAS_RESUMEN"/>-1 semanas hasta mañana (exclusivo).</summary>
        public static DateTime InicioResumen(DateTime hoy) => Lunes(hoy).AddDays(-7 * (SEMANAS_RESUMEN - 1));

        private enum Tipo { Ninguna, Anadido, NoLeInteresa, Quitada }

        private static Tipo TipoRespuesta(FilaRespuestaSugerencia f)
        {
            if (RespuestasSugerencia.EsQuitada(f.Respuesta, f.Texto))
            {
                return Tipo.Quitada;
            }
            if (f.Respuesta == RespuestasSugerencia.ANADIDO)
            {
                return Tipo.Anadido;
            }
            return f.Respuesta == RespuestasSugerencia.NO_LE_INTERESA ? Tipo.NoLeInteresa : Tipo.Ninguna;
        }

        private static bool EsAnadidaEnPedido(FilaRespuestaSugerencia f) => TipoRespuesta(f) == Tipo.Anadido && f.EntroEnPedido == true;

        public static ResumenSugerenciasVendedorDTO CalcularResumen(IEnumerable<FilaRespuestaSugerencia> filas, DateTime hoy)
        {
            List<FilaRespuestaSugerencia> lista = (filas ?? Enumerable.Empty<FilaRespuestaSugerencia>()).Where(f => f != null).ToList();
            DateTime primerLunes = InicioResumen(hoy);
            var resumen = new ResumenSugerenciasVendedorDTO();
            for (int i = 0; i < SEMANAS_RESUMEN; i++)
            {
                DateTime desde = primerLunes.AddDays(7 * i);
                DateTime hasta = desde.AddDays(7);
                resumen.Semanas.Add(Semana(lista.Where(f => f.FechaMostrada >= desde && f.FechaMostrada < hasta), desde, hasta));
            }
            resumen.EstaSemana = resumen.Semanas.Last();

            int aceptadas = resumen.Semanas.Sum(s => s.Anadidas);
            int ofrecidas = aceptadas + resumen.Semanas.Sum(s => s.NoLeInteresa);
            resumen.AceptadasDeCadaDiez = ofrecidas < MINIMO_OFRECIDAS_PARA_ACIERTO
                ? (int?)null
                : (int)Math.Round(10m * aceptadas / ofrecidas, 0, MidpointRounding.AwayFromZero);
            resumen.Frase = Frase(resumen.EstaSemana);
            resumen.Ayuda = Ayuda(resumen);
            return resumen;
        }

        private static ResumenSemanaSugerenciasDTO Semana(IEnumerable<FilaRespuestaSugerencia> filas, DateTime desde, DateTime hasta)
        {
            List<FilaRespuestaSugerencia> lista = filas.ToList();
            var semana = new ResumenSemanaSugerenciasDTO
            {
                Desde = desde,
                Hasta = hasta,
                Mostradas = lista.Count,
                Anadidas = lista.Count(f => TipoRespuesta(f) == Tipo.Anadido),
                NoLeInteresa = lista.Count(f => TipoRespuesta(f) == Tipo.NoLeInteresa),
                Quitadas = lista.Count(f => TipoRespuesta(f) == Tipo.Quitada),
                AnadidasEnPedido = lista.Count(EsAnadidaEnPedido),
                ImporteVentas = lista.Where(EsAnadidaEnPedido).Sum(f => f.ImporteEnPedido ?? 0m)
            };
            semana.Respuestas = semana.Anadidas + semana.NoLeInteresa + semana.Quitadas;
            return semana;
        }

        private static string Plural(int n, string singular, string plural) => n == 1 ? singular : plural;

        private static string Euros(decimal importe) => importe.ToString("N2", ES) + " €";

        internal static string Frase(ResumenSemanaSugerenciasDTO semana)
        {
            if (semana == null || semana.Respuestas == 0)
            {
                return FRASE_ANIMO;
            }
            if (semana.Anadidas > 0)
            {
                string ventas = semana.ImporteVentas > 0 ? $", {Euros(semana.ImporteVentas)} en ventas" : string.Empty;
                return $"Esta semana: {semana.Anadidas} {Plural(semana.Anadidas, "producto añadido", "productos añadidos")} desde las sugerencias{ventas}. {FRASE_ANIMO}";
            }
            return $"Esta semana ha contestado {semana.Respuestas} {Plural(semana.Respuestas, "sugerencia", "sugerencias")}: gracias. {FRASE_ANIMO}";
        }

        private static string Ayuda(ResumenSugerenciasVendedorDTO resumen)
        {
            var texto = new StringBuilder();
            texto.AppendLine("Sus respuestas en las últimas semanas:");
            foreach (ResumenSemanaSugerenciasDTO semana in resumen.Semanas)
            {
                string cuando = semana == resumen.EstaSemana ? "Esta semana" : "Semana del " + semana.Desde.ToString("dd/MM", ES);
                string ventas = semana.AnadidasEnPedido > 0
                    ? $" ({semana.AnadidasEnPedido} en el pedido, {Euros(semana.ImporteVentas)})"
                    : string.Empty;
                texto.AppendLine($"  {cuando}: {semana.Anadidas} ✓{ventas}, {semana.NoLeInteresa} ✗, {semana.Quitadas} 🚫");
            }
            if (resumen.AceptadasDeCadaDiez.HasValue)
            {
                texto.AppendLine($"De cada 10 que ha ofrecido, le han aceptado {resumen.AceptadasDeCadaDiez.Value}.");
            }
            texto.AppendLine();
            texto.AppendLine("✓ si lo quiere, ✗ si no le interesa, 🚫 si no se lo ofrece. Con lo que marca, las sugerencias aprenden qué ofrecer a cada cliente; lo que no se marca no cuenta.");
            texto.Append("Sus respuestas cuentan para el agradecimiento a quienes más ayudan, en las Novedades de cada versión.");
            return texto.ToString();
        }

        public static ParticipacionSugerenciasDTO CalcularParticipacion(IEnumerable<FilaRespuestaSugerencia> filas, DateTime desde, DateTime hasta)
        {
            List<FilaRespuestaSugerencia> respondidas = (filas ?? Enumerable.Empty<FilaRespuestaSugerencia>())
                .Where(f => f != null && TipoRespuesta(f) != Tipo.Ninguna)
                .ToList();
            var resultado = new ParticipacionSugerenciasDTO
            {
                Desde = desde,
                Hasta = hasta,
                Reglas = $"✓ {PUNTOS_ANADIDO} puntos (+{PUNTOS_EXTRA_ANADIDO_EN_PEDIDO} si acaba en el pedido), ✗ {PUNTOS_NO_LE_INTERESA}, " +
                    $"🚫 {PUNTOS_QUITADA}. Como mucho {MAXIMO_RESPUESTAS_CON_PUNTOS_AL_DIA} respuestas con puntos por persona y día. " +
                    "Lo que entra en el pedido sin pulsar ✓ no da puntos."
            };
            foreach (IGrouping<string, FilaRespuestaSugerencia> grupo in respondidas
                .GroupBy(f => SinDominio(f.Usuario) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var usuario = new ParticipacionSugerenciasUsuarioDTO
                {
                    Usuario = string.IsNullOrWhiteSpace(grupo.Key) ? InformeUsoSugerenciasProductos.SIN_VENDEDOR : grupo.Key,
                    Anadidas = grupo.Count(f => TipoRespuesta(f) == Tipo.Anadido),
                    NoLeInteresa = grupo.Count(f => TipoRespuesta(f) == Tipo.NoLeInteresa),
                    Quitadas = grupo.Count(f => TipoRespuesta(f) == Tipo.Quitada),
                    AnadidasEnPedido = grupo.Count(EsAnadidaEnPedido),
                    ImporteVentas = grupo.Where(EsAnadidaEnPedido).Sum(f => f.ImporteEnPedido ?? 0m)
                };
                foreach (IGrouping<DateTime, FilaRespuestaSugerencia> dia in grupo.GroupBy(f => (f.FechaRespuesta ?? f.FechaMostrada).Date))
                {
                    List<FilaRespuestaSugerencia> ordenadas = dia.OrderBy(f => f.FechaRespuesta ?? f.FechaMostrada).ToList();
                    usuario.Puntos += ordenadas.Take(MAXIMO_RESPUESTAS_CON_PUNTOS_AL_DIA).Sum(Puntos);
                    usuario.RespuestasSinPuntos += Math.Max(0, ordenadas.Count - MAXIMO_RESPUESTAS_CON_PUNTOS_AL_DIA);
                }
                resultado.PorUsuario.Add(usuario);
            }
            resultado.PorUsuario = resultado.PorUsuario
                .OrderByDescending(u => u.Puntos)
                .ThenBy(u => u.Usuario, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return resultado;
        }

        /// <summary>Lo que vale una respuesta (sin el tope diario).</summary>
        public static int Puntos(FilaRespuestaSugerencia fila)
        {
            switch (TipoRespuesta(fila))
            {
                case Tipo.Anadido:
                    return PUNTOS_ANADIDO + (fila.EntroEnPedido == true ? PUNTOS_EXTRA_ANADIDO_EN_PEDIDO : 0);
                case Tipo.NoLeInteresa:
                    return PUNTOS_NO_LE_INTERESA;
                case Tipo.Quitada:
                    return PUNTOS_QUITADA;
                default:
                    return 0;
            }
        }
    }
}
