using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    public interface IServicioFrasesRitmo
    {
        /// <summary>La frase del panel de ritmo. Nunca lanza: si algo falla, la plantilla fija del corte 1.</summary>
        Task<string> Generar(string vendedor, RitmoContactosDTO ritmo, DateTime ahora, Func<DateTime, bool> esLaborable);
    }

    /// <summary>NestoAPI#603 (corte 4): una frase elegida (fila de FrasesRitmo). <see cref="Texto"/> lleva las variables sin sustituir.</summary>
    public class FraseRitmoGuardada
    {
        public string Vendedor { get; set; }
        public DateTime Fecha { get; set; }
        public string Situacion { get; set; }
        /// <summary>Clave de la plantilla del banco, o «OpenAI».</summary>
        public string Plantilla { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#603 (corte 4): lo que la situación necesita además del ritmo.</summary>
    public class DatosFrasesRitmo
    {
        /// <summary>Contactos (rapports Estado 0 T/V/W) de cada día anterior a hoy, de los últimos 62 días.</summary>
        public Dictionary<DateTime, int> ContactosPorDia { get; set; } = new Dictionary<DateTime, int>();
        /// <summary>Rapports de hoy del vendedor marcados con Pedido.</summary>
        public int PedidosHoy { get; set; }
        /// <summary>Vendedores.Descripción.</summary>
        public string Descripcion { get; set; }
    }

    public interface IRepositorioFrasesRitmo
    {
        Task<DatosFrasesRitmo> LeerDatos(string vendedor, DateTime hoy);
        /// <summary>Las últimas <paramref name="numero"/> frases del vendedor, de la más nueva a la más vieja.</summary>
        Task<List<FraseRitmoGuardada>> LeerUltimas(string vendedor, int numero);
        Task Guardar(FraseRitmoGuardada frase);
    }

    /// <summary>
    /// NestoAPI#603 (corte 4): la frase del panel de ritmo. Una por vendedor, día y situación: mientras la situación no
    /// cambie (p. ej. de «por debajo» a «objetivo cumplido»), se repite la misma plantilla con los números al día. Cuando
    /// cambia, se elige otra sin repetir las 10 últimas del vendedor. La primera del día se le pide a OpenAI si hay clave
    /// (una llamada por vendedor y día como mucho); si falla o no vale, plantilla del banco.
    /// </summary>
    public class ServicioFrasesRitmo : IServicioFrasesRitmo
    {
        private readonly IRepositorioFrasesRitmo repositorio;
        private readonly Func<IServicioOpenAI> crearOpenAI;

        public ServicioFrasesRitmo() : this(new RepositorioFrasesRitmoSql(), CrearOpenAISiHayClave)
        {
        }

        internal ServicioFrasesRitmo(IRepositorioFrasesRitmo repositorio, Func<IServicioOpenAI> crearOpenAI)
        {
            this.repositorio = repositorio;
            this.crearOpenAI = crearOpenAI ?? (() => null);
        }

        internal static IServicioOpenAI CrearOpenAISiHayClave()
        {
            return string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["OpenAIKey"]) ? null : new ServicioOpenAI();
        }

        public async Task<string> Generar(string vendedor, RitmoContactosDTO ritmo, DateTime ahora, Func<DateTime, bool> esLaborable)
        {
            try
            {
                return await GenerarSinProteger(vendedor, ritmo, ahora, esLaborable).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Frases de ritmo #603] No se pudo generar la frase de {vendedor}: {ex.Message}", ex), "Sistema (frases de ritmo)");
                return ritmo?.Frase;
            }
        }

        private async Task<string> GenerarSinProteger(string vendedor, RitmoContactosDTO ritmo, DateTime ahora, Func<DateTime, bool> esLaborable)
        {
            DateTime hoy = ahora.Date;
            DatosFrasesRitmo datos = await repositorio.LeerDatos(vendedor, hoy).ConfigureAwait(false) ?? new DatosFrasesRitmo();
            SituacionRitmo situacion = GeneradorFrasesRitmo.CalcularSituacion(ritmo, ahora, esLaborable, datos.ContactosPorDia,
                datos.PedidosHoy, GeneradorFrasesRitmo.NombreDePila(datos.Descripcion));

            // Sin la tabla (script sin lanzar) o con la BD con problemas: plantilla sin memoria, y queda en ELMAH.
            List<FraseRitmoGuardada> ultimas;
            bool hayMemoria = true;
            try
            {
                ultimas = await repositorio.LeerUltimas(vendedor, GeneradorFrasesRitmo.FRASES_EN_MEMORIA + 10).ConfigureAwait(false)
                    ?? new List<FraseRitmoGuardada>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Frases de ritmo #603] No se pudo leer FrasesRitmo de {vendedor}: {ex.Message}", ex), "Sistema (frases de ritmo)");
                ultimas = new List<FraseRitmoGuardada>();
                hayMemoria = false;
            }

            List<FraseRitmoGuardada> deHoy = ultimas.Where(f => f.Fecha.Date == hoy).ToList();
            FraseRitmoGuardada mismaSituacion = deHoy.FirstOrDefault(f => f.Situacion == situacion.Clave);
            if (mismaSituacion != null)
            {
                return GeneradorFrasesRitmo.Sustituir(mismaSituacion.Texto, situacion);
            }

            List<FraseRitmoGuardada> recientes = ultimas.Take(GeneradorFrasesRitmo.FRASES_EN_MEMORIA).ToList();
            FraseRitmoGuardada nueva = null;

            bool openAIUsadoHoy = deHoy.Any(f => f.Plantilla == GeneradorFrasesRitmo.PLANTILLA_OPENAI);
            if (hayMemoria && !openAIUsadoHoy)
            {
                string fraseIA = await PedirAOpenAI(vendedor, situacion, recientes.Select(f => f.Texto).ToList()).ConfigureAwait(false);
                if (fraseIA != null)
                {
                    nueva = new FraseRitmoGuardada { Plantilla = GeneradorFrasesRitmo.PLANTILLA_OPENAI, Texto = fraseIA };
                }
            }

            if (nueva == null)
            {
                PlantillaFraseRitmo plantilla = GeneradorFrasesRitmo.Elegir(situacion, recientes.Select(f => f.Plantilla).ToList(), vendedor, hoy);
                if (plantilla == null)
                {
                    return ritmo?.Frase;
                }
                nueva = new FraseRitmoGuardada { Plantilla = plantilla.Clave, Texto = plantilla.Texto };
            }

            nueva.Vendedor = vendedor;
            nueva.Fecha = ahora;
            nueva.Situacion = situacion.Clave;
            if (hayMemoria)
            {
                try
                {
                    await repositorio.Guardar(nueva).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Frases de ritmo #603] No se pudo guardar la frase de {vendedor}: {ex.Message}", ex), "Sistema (frases de ritmo)");
                }
            }
            return GeneradorFrasesRitmo.Sustituir(nueva.Texto, situacion);
        }

        private async Task<string> PedirAOpenAI(string vendedor, SituacionRitmo situacion, List<string> recientes)
        {
            try
            {
                IServicioOpenAI openAI = crearOpenAI();
                if (openAI == null)
                {
                    return null;
                }
                string respuesta = await openAI.GenerarContenidoAsync(GeneradorFrasesRitmo.PROMPT_SISTEMA_OPENAI,
                    GeneradorFrasesRitmo.MensajeOpenAI(situacion, recientes), maxTokens: 120, temperature: 0.9).ConfigureAwait(false);
                return GeneradorFrasesRitmo.ValidarFraseOpenAI(respuesta, situacion, recientes);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Frases de ritmo #603] OpenAI no ha dado frase para {vendedor}: {ex.Message}", ex), "Sistema (frases de ritmo)");
                return null;
            }
        }
    }

    /// <summary>NestoAPI#603 (corte 4): FrasesRitmo y los contactos por día (SqlClient, como la cartera).</summary>
    public class RepositorioFrasesRitmoSql : IRepositorioFrasesRitmo
    {
        internal const string SQL_DATOS = @"
            SET NOCOUNT ON;
            SELECT CAST(s.Fecha AS date) Dia, COUNT(*) Contactos
            FROM SeguimientoCliente s WITH (NOLOCK)
            WHERE s.Vendedor = @Vendedor AND s.Fecha >= @Desde AND s.Fecha < @Hoy AND s.Estado = 0 AND s.Tipo IN ('T', 'V', 'W')
            GROUP BY CAST(s.Fecha AS date);

            SELECT COUNT(*) FROM SeguimientoCliente s WITH (NOLOCK)
            WHERE s.Vendedor = @Vendedor AND s.Fecha >= @Hoy AND s.Fecha < @Manana AND s.Pedido = 1;

            SELECT TOP 1 RTRIM(v.Descripción) FROM Vendedores v WITH (NOLOCK) WHERE v.Empresa = '1' AND v.Número = @Vendedor;";

        internal const string SQL_ULTIMAS = @"
            SELECT TOP (@Numero) Vendedor, Fecha, Situacion, Plantilla, Texto
            FROM dbo.FrasesRitmo WITH (NOLOCK)
            WHERE Vendedor = @Vendedor
            ORDER BY Fecha DESC, Id DESC;";

        internal const string SQL_GUARDAR = @"
            INSERT INTO dbo.FrasesRitmo (Fecha, Vendedor, Situacion, Plantilla, Texto)
            VALUES (@Fecha, @Vendedor, @Situacion, @Plantilla, @Texto);";

        private const int TIMEOUT_SEGUNDOS = 30;

        public Task<DatosFrasesRitmo> LeerDatos(string vendedor, DateTime hoy)
        {
            return Task.Run(() =>
            {
                var datos = new DatosFrasesRitmo();
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(SQL_DATOS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = hoy.Date.AddDays(-62) });
                    _ = comando.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.DateTime) { Value = hoy.Date });
                    _ = comando.Parameters.Add(new SqlParameter("@Manana", SqlDbType.DateTime) { Value = hoy.Date.AddDays(1) });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            datos.ContactosPorDia[Convert.ToDateTime(lector["Dia"]).Date] = Convert.ToInt32(lector["Contactos"]);
                        }
                        if (lector.NextResult() && lector.Read())
                        {
                            datos.PedidosHoy = Convert.ToInt32(lector[0]);
                        }
                        if (lector.NextResult() && lector.Read() && lector[0] != DBNull.Value)
                        {
                            datos.Descripcion = lector[0].ToString().Trim();
                        }
                    }
                }
                return datos;
            });
        }

        public Task<List<FraseRitmoGuardada>> LeerUltimas(string vendedor, int numero)
        {
            return Task.Run(() =>
            {
                var frases = new List<FraseRitmoGuardada>();
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(SQL_ULTIMAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Numero", SqlDbType.Int) { Value = numero });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            frases.Add(new FraseRitmoGuardada
                            {
                                Vendedor = lector["Vendedor"].ToString().Trim(),
                                Fecha = Convert.ToDateTime(lector["Fecha"]),
                                Situacion = lector["Situacion"].ToString().Trim(),
                                Plantilla = lector["Plantilla"].ToString().Trim(),
                                Texto = lector["Texto"].ToString()
                            });
                        }
                    }
                }
                return frases;
            });
        }

        public Task Guardar(FraseRitmoGuardada frase)
        {
            return Task.Run(() =>
            {
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand(SQL_GUARDAR, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = frase.Fecha });
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.Char, 3) { Value = frase.Vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Situacion", SqlDbType.VarChar, 30) { Value = frase.Situacion });
                    _ = comando.Parameters.Add(new SqlParameter("@Plantilla", SqlDbType.VarChar, 20) { Value = frase.Plantilla });
                    _ = comando.Parameters.Add(new SqlParameter("@Texto", SqlDbType.NVarChar, 400) { Value = frase.Texto });
                    _ = comando.ExecuteNonQuery();
                }
            });
        }
    }

    /// <summary>NestoAPI#603: conexión SqlClient con la cadena de NVEntities (como RepositorioCarteraContactoSql).</summary>
    internal static class ConexionSql
    {
        internal static SqlConnection Abrir()
        {
            string cadena;
            using (var contexto = new NVEntities())
            {
                cadena = contexto.Database.Connection.ConnectionString;
            }
            var conexion = new SqlConnection(cadena);
            conexion.Open();
            return conexion;
        }
    }
}
