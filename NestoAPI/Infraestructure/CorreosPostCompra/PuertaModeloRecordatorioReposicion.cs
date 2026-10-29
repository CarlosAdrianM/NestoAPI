using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>NestoAPI#736: la P1 del modelo 1 de propensión (#671) para los productos que se le recordarían a un cliente.</summary>
    public interface IPuntuadorReposicion
    {
        /// <summary>La versión del modelo que se usaría ahora, o null si no hay (interruptor, sin modelo o no carga). Nunca lanza.</summary>
        string ModeloDisponible();

        /// <summary>
        /// P1 de cada producto que el modelo sabe puntuar (los que no son sugeribles no salen). Puede lanzar: la puerta lo trata
        /// como modelo no disponible.
        /// </summary>
        Task<Dictionary<string, float>> Puntuar(string empresa, string cliente, DateTime hoy, IReadOnlyCollection<string> productos);
    }

    /// <summary>NestoAPI#736: lo que ha hecho la puerta del modelo en una pasada (para el resumen del job).</summary>
    public class ResumenPuertaModeloDTO
    {
        public ModoPuertaModeloReposicion Modo { get; set; }
        public decimal Umbral { get; set; }
        /// <summary>Null si no se ha puntuado (apagada, sin modelo o ha fallado).</summary>
        public string VersionModelo { get; set; }
        /// <summary>Productos (de los correos que se enviarían y del grupo de control) con P1.</summary>
        public int Puntuados { get; set; }
        /// <summary>Productos que el modelo no puntúa (no son sugeribles): pasan siempre.</summary>
        public int SinPuntuar { get; set; }
        /// <summary>Productos con P1 menor que el umbral: en Sombra, los que habría quitado; en Activo, los quitados.</summary>
        public int PorDebajo { get; set; }
        /// <summary>Activo: correos (o apuntes del grupo de control) que se han quedado sin ningún producto.</summary>
        public int CorreosQuitados { get; set; }
        /// <summary>Por qué no se ha puntuado (sin modelo o fallo). Null si ha ido bien o está apagada.</summary>
        public string Aviso { get; set; }
    }

    /// <summary>
    /// NestoAPI#736: puerta del correo del recordatorio de reposición con el modelo 1 de propensión. Se aplica DESPUÉS de las
    /// reglas de envío (bajas, repeticiones, vendedores), a los correos que se enviarían y al grupo de control registrado, para
    /// que la comparación Enviado/Control siga siendo con clientes iguales. Solo el correo: la push no pasa por aquí.
    /// <list type="bullet">
    /// <item>Apagado: nada.</item>
    /// <item>Sombra (por defecto): cada producto lleva su P1 y si habría quedado fuera (<c>ProbabilidadModelo</c> y
    /// <c>FueraPorModelo</c>, que se registran en RecordatoriosReposicionEnvios), sin quitar nada.</item>
    /// <item>Activo: además, los de P1 menor que el umbral van a Descartes con su motivo; el correo que se queda sin nada no sale.</item>
    /// </list>
    /// Sin modelo, o si falla al puntuar cualquier cliente: no se quita ni se apunta nada (como antes de #736) y una línea a
    /// ELMAH, una sola vez por motivo.
    /// </summary>
    public static class PuertaModeloRecordatorioReposicion
    {
        public const string MOTIVO_MODELO_PREFIJO = "El modelo de propensión le da P1 = ";
        internal const string AVISO_SIN_MODELO = "No hay modelo de propensión disponible (interruptor ModeloSugerenciasActivo, ningún modelo que " +
            "pase la puerta o no carga): no se ha puntuado nada, como antes de NestoAPI#736.";
        internal const string AVISO_FALLO = "Ha fallado el modelo de propensión al puntuar: no se ha quitado ni apuntado nada, como antes de NestoAPI#736. ";

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");
        private static readonly HashSet<string> avisados = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>«El modelo de propensión le da P1 = 0,080: menos de 0,15.»</summary>
        public static string MotivoModelo(float p1, decimal umbral)
            => $"{MOTIVO_MODELO_PREFIJO}{p1.ToString("0.000", ES)}: menos de {umbral.ToString("0.00", ES)}.";

        public static bool PorDebajo(float p1, decimal umbral) => (decimal)p1 < umbral;

        /// <param name="registrarError">Null: ELMAH.</param>
        public static async Task<ResumenPuertaModeloDTO> Aplicar(PlanRecordatorioReposicionDTO plan, ResultadoRecordatorioReposicionDTO resultado,
            ModoPuertaModeloReposicion modo, decimal umbral, IPuntuadorReposicion puntuador, string empresa, DateTime hoy,
            Action<Exception> registrarError = null)
        {
            var resumen = new ResumenPuertaModeloDTO { Modo = modo, Umbral = umbral };
            if (modo == ModoPuertaModeloReposicion.Apagado || plan == null || puntuador == null)
            {
                return resumen;
            }
            List<RecordatorioReposicionClienteDTO> correos = plan.Enviar.Concat(plan.Control)
                .Where(c => c?.Productos != null && c.Productos.Count > 0)
                .ToList();
            if (correos.Count == 0)
            {
                return resumen;
            }

            string version;
            try
            {
                version = puntuador.ModeloDisponible();
            }
            catch (Exception)
            {
                version = null;
            }
            if (version == null)
            {
                resumen.Aviso = AVISO_SIN_MODELO;
                AvisarUnaVez("sin-modelo", registrarError, new Exception("[Recordatorio reposición #736] " + AVISO_SIN_MODELO));
                return resumen;
            }

            // Primero todas las P1; solo si no ha fallado nada se apuntan (todo o nada)
            var puntuaciones = new Dictionary<RecordatorioReposicionClienteDTO, Dictionary<string, float>>();
            try
            {
                foreach (RecordatorioReposicionClienteDTO correo in correos)
                {
                    List<string> productos = correo.Productos.Select(p => p.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                    Dictionary<string, float> p1 = await puntuador.Puntuar(empresa, correo.Cliente?.Trim(), hoy.Date, productos).ConfigureAwait(false);
                    puntuaciones[correo] = p1 == null
                        ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, float>(p1, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                resumen.Aviso = AVISO_FALLO + ex.Message;
                AvisarUnaVez("fallo|" + version, registrarError, new Exception("[Recordatorio reposición #736] " + AVISO_FALLO + ex.Message, ex));
                return resumen;
            }

            resumen.VersionModelo = version;
            foreach (RecordatorioReposicionClienteDTO correo in correos)
            {
                foreach (CandidatoReposicionDTO producto in correo.Productos)
                {
                    if (!puntuaciones[correo].TryGetValue(producto.Producto?.Trim() ?? string.Empty, out float p1) || float.IsNaN(p1))
                    {
                        resumen.SinPuntuar++;
                        continue;
                    }
                    producto.ProbabilidadModelo = p1;
                    producto.FueraPorModelo = PorDebajo(p1, umbral);
                    resumen.Puntuados++;
                    if (producto.FueraPorModelo == true)
                    {
                        resumen.PorDebajo++;
                    }
                }
            }

            if (modo == ModoPuertaModeloReposicion.Activo)
            {
                foreach (RecordatorioReposicionClienteDTO correo in correos)
                {
                    List<CandidatoReposicionDTO> fuera = correo.Productos.Where(p => p.FueraPorModelo == true).ToList();
                    foreach (CandidatoReposicionDTO producto in fuera)
                    {
                        producto.Motivo = MotivoModelo(producto.ProbabilidadModelo.Value, umbral);
                        resultado?.Descartes.Add(producto);
                    }
                    correo.Productos = correo.Productos.Where(p => p.FueraPorModelo != true).ToList();
                }
                resumen.CorreosQuitados = plan.Enviar.RemoveAll(c => c.Productos.Count == 0) + plan.Control.RemoveAll(c => c.Productos.Count == 0);
            }
            return resumen;
        }

        private static void AvisarUnaVez(string clave, Action<Exception> registrarError, Exception ex)
        {
            lock (avisados)
            {
                if (!avisados.Add(clave))
                {
                    return;
                }
            }
            try
            {
                (registrarError ?? ElmahHelper.Log)(ex);
            }
            catch (Exception)
            {
                // El aviso es una ayuda: nunca rompe el job
            }
        }

        /// <summary>Para los tests: que el siguiente aviso vuelva a salir.</summary>
        internal static void OlvidarAvisos()
        {
            lock (avisados)
            {
                avisados.Clear();
            }
        }
    }
}
