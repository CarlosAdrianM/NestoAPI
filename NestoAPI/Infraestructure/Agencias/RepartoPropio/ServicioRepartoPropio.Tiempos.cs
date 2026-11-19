using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio.ModeloTiemposParada;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#881 (corte 3): los tiempos reales de cada parada (dbo.RepartoParadasTiempos) y las fotos de la previsión de fin de ruta
    /// (dbo.RepartoEstimaciones), para medir previsto contra real y alimentar el modelo en sombra. Se escriben DESPUÉS del OK y nunca rompen
    /// nada (ELMAH). Sin las tablas, nada (ELMAH una vez).
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        /// <summary>Como mucho una foto por repartidor en este tiempo (si llega otra antes, se actualiza la última).</summary>
        internal const int MINUTOS_ENTRE_FOTOS = 10;
        private static bool sinTablaTiemposAvisado;
        private static bool sinTablaEstimacionesAvisado;

        private bool TablaTiempos()
        {
            if (paradas.TiemposReales == null)
            {
                return false;
            }
            if (paradas.TiemposReales.TablaTiemposExiste())
            {
                return true;
            }
            if (!sinTablaTiemposAvisado)
            {
                sinTablaTiemposAvisado = true;
                ElmahHelper.Log(new Exception("[Reparto #881] Falta dbo.RepartoParadasTiempos (Scripts/Issue881_RepartoFinRutaYTiempos.sql): no se guardan los tiempos reales (se avisa una vez)."));
            }
            return false;
        }

        /// <summary>La foto de una previsión (Estimacion u OrdenSugerido). Nunca lanza.</summary>
        private async Task FotoEstimacion(EstimacionFinRutaDTO e, string usuario, string origenRuta, string calculo)
        {
            if (e == null || paradas.TiemposReales == null || string.IsNullOrWhiteSpace(usuario))
            {
                return;
            }
            try
            {
                if (!paradas.TiemposReales.TablaEstimacionesExiste())
                {
                    if (!sinTablaEstimacionesAvisado)
                    {
                        sinTablaEstimacionesAvisado = true;
                        ElmahHelper.Log(new Exception("[Reparto #881] Falta dbo.RepartoEstimaciones (Scripts/Issue881_RepartoFinRutaYTiempos.sql): no se guardan las fotos de la previsión (se avisa una vez)."));
                    }
                    return;
                }
                await paradas.TiemposReales.RegistrarEstimacion(new FotoEstimacionReparto
                {
                    Repartidor = NombreRepartidor(usuario),
                    FechaHora = ahora(),
                    HoraSalida = e.HoraSalida,
                    NumeroParadas = e.NumeroParadas,
                    MinutosConduccion = e.MinutosConduccion,
                    MinutosParadas = e.MinutosParadas,
                    MinutosDescarga = e.MinutosDescarga,
                    Km = e.Km,
                    FinEstimado = e.FinEstimado,
                    HoraCierre = e.HoraCierre,
                    PasaDelCierre = e.PasaDelCierre,
                    Aproximada = e.Aproximada,
                    Motor = e.Motor,
                    OrigenRuta = origenRuta,
                    Destino = e.Destino,
                    Calculo = calculo,
                    MinutosDe = e.MinutosDe
                }, MINUTOS_ENTRE_FOTOS).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] No se pudo guardar la foto del fin de ruta de {NombreRepartidor(usuario)}: {ex.Message}", ex));
            }
        }

        /// <summary>Lo que llega de la PDA para el tiempo de una parada.</summary>
        private class DatosTiempoParada
        {
            public Guid Clave;
            public string Resultado;
            public DateTime Fin;
            public DateTime? Ir;
            public DateTime? Llegada;
            public decimal? Latitud;
            public decimal? Longitud;
            public string FormaCobro;
            public decimal? ImporteCobrado;
            public int? BultosRecogidos;
        }

        /// <summary>Después de una entrega, de una incidencia o de cerrar una parada al recoger: su fila de tiempos. Nunca lanza.</summary>
        private async Task ApuntarTiempoParada(int envioId, DatosTiempoParada datos, string usuario, int? traspaso = null, int? bultos = null)
        {
            if (datos == null || datos.Clave == Guid.Empty || string.IsNullOrWhiteSpace(usuario))
            {
                return;
            }
            try
            {
                if (!TablaTiempos())
                {
                    return;
                }
                ConstantesTiemposReparto constantes = ConstantesTiempos();
                var fila = new TiempoParadaReparto
                {
                    ClaveIdempotencia = datos.Clave,
                    Repartidor = NombreRepartidor(usuario),
                    Dia = datos.Fin.Date,
                    Resultado = datos.Resultado,
                    EnvioId = envioId == 0 ? (int?)null : envioId,
                    FechaHoraIr = datos.Ir,
                    FechaHoraLlegada = datos.Llegada,
                    FechaHoraFin = datos.Fin,
                    Latitud = CoordenadasGeo.Validas(datos.Latitud, datos.Longitud) ? datos.Latitud : null,
                    Longitud = CoordenadasGeo.Validas(datos.Latitud, datos.Longitud) ? datos.Longitud : null,
                    Traspaso = traspaso,
                    Bultos = bultos
                };
                var parada = new EnvioRepartoDTO { EnvioId = envioId, TipoParada = TiposParadaReparto.ENVIO };
                if (NucleoParadasReparto.EsReposicion(envioId) || datos.Resultado == TiempoParadaReparto.RESULTADO_RECOGIDA)
                {
                    parada.TipoParada = TiposParadaReparto.REPOSICION;
                }
                else if (envioId > 0)
                {
                    EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
                    if (envio != null)
                    {
                        fila.Empresa = envio.Empresa?.Trim();
                        fila.Cliente = envio.Cliente?.Trim();
                        fila.Contacto = envio.Contacto?.Trim();
                        fila.CodigoPostal = envio.CodPostal?.Trim();
                        fila.Pedido = envio.Pedido;
                        fila.Bultos = envio.Bultos;
                        parada.Pedido = envio.Pedido;
                        parada.Bultos = envio.Bultos;
                        parada.Reembolso = envio.Reembolso;
                        NotaParadaReparto nota = await NotaDe(envio.Pedido).ConfigureAwait(false);
                        if (nota != null)
                        {
                            parada.TipoParada = nota.TipoParada;
                        }
                        if (paradas.ServicioTecnico != null)
                        {
                            await paradas.ServicioTecnico.Completar(new List<EnvioRepartoDTO> { parada }).ConfigureAwait(false);
                        }
                    }
                }
                if (parada.TipoParada == TiposParadaReparto.NOTA_RECOGIDA && datos.BultosRecogidos.HasValue)
                {
                    fila.Bultos = datos.BultosRecogidos;
                }
                fila.TipoParada = datos.Resultado == TiempoParadaReparto.RESULTADO_RECOGIDA ? TiempoParadaReparto.TIPO_RECOGIDA_REPOSICION : parada.TipoParada;
                fila.TipoServicioTecnicoId = parada.TipoServicioTecnicoId;
                fila.ServicioTecnico = parada.ServicioTecnico;
                fila.MinutosServicioTecleados = parada.MinutosServicioTecleados;
                bool conCobro = parada.Reembolso > 0 || parada.TipoParada == TiposParadaReparto.NOTA_COBRO;
                if (conCobro && datos.Resultado == TiempoParadaReparto.RESULTADO_ENTREGADA)
                {
                    fila.FormaCobro = string.IsNullOrWhiteSpace(datos.FormaCobro) ? PruebaEntregaReparto.COBRO_NO_COBRADO : datos.FormaCobro.Trim();
                    fila.ImporteCobro = datos.ImporteCobrado ?? (parada.Reembolso > 0 ? parada.Reembolso : (decimal?)null);
                }
                fila.MinutosPrevistos = (decimal)MinutosParadaReparto.De(ParadaParaTiempo.De(parada), constantes);
                _ = await paradas.TiemposReales.RegistrarTiempo(fila).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] La parada {envioId} está cerrada, pero no se pudo guardar su tiempo: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// NestoAPI#881: con «Reparto.TiempoParadaQuienManda» = «Modelo» y modelo guardado, los minutos de cada parada del modelo (los que la
        /// regla cuenta como 0 por estar en la misma dirección siguen a 0; los tecleados en la nota mandan). Sin modelo o si falla, la regla.
        /// </summary>
        private List<double> MinutosConModeloSiManda(IReadOnlyList<EnvioRepartoDTO> lista, List<double> regla, ConstantesTiemposReparto constantes,
            string usuario, DateTime horaSalida, out bool delModelo)
        {
            delModelo = false;
            if (!constantes.MandaElModelo || paradas.MinutosModelo == null || lista.Count == 0)
            {
                return regla;
            }
            var resultado = new List<double>(regla);
            for (int i = 0; i < lista.Count; i++)
            {
                EnvioRepartoDTO p = lista[i];
                if (regla[i] <= 0 || p.MinutosServicioTecleados.HasValue)
                {
                    continue;
                }
                double? minutos = paradas.MinutosModelo.Minutos(ConstructorRasgosTiemposParada.Rasgos(p.TipoParada, p.ServicioTecnico, p.Bultos,
                    p.Reembolso > 0 || p.EsNotaCobro ? "?" : null, p.Reembolso, p.CodigoPostal, horaSalida, usuario, p.MinutosServicioTecleados));
                if (!minutos.HasValue)
                {
                    return regla; // sin modelo (o falla): todo con la regla
                }
                resultado[i] = minutos.Value;
                delModelo = true;
            }
            return resultado;
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisosTiempos()
        {
            sinTablaTiemposAvisado = false;
            sinTablaEstimacionesAvisado = false;
        }
    }
}
