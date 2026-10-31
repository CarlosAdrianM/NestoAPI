using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#770: dónde se guardan las firmas y cómo se saca la dirección del sello. Vacío: sin blob ni dirección.</summary>
    public class DependenciasFirmasReparto
    {
        /// <summary>Null o sin configurar: la firma se queda en la columna Firma (como antes de #770).</summary>
        public IAlmacenFirmasEntrega Almacen { get; set; }
        /// <summary>Null: el sello lleva solo las coordenadas.</summary>
        public IGeocodificadorInverso Geocodificador { get; set; }
        /// <summary>Dónde se apuntan los fallos (ELMAH). Nunca con la firma ni el DNI.</summary>
        public Action<Exception> RegistrarError { get; set; } = ElmahHelper.Log;
        /// <summary>El tope de la geocodificación inversa, aunque el geocodificador no lo respete.</summary>
        public TimeSpan TiempoMaximoDireccion { get; set; } = GeocodificadorInversoNominatim.TIEMPO_MAXIMO + TimeSpan.FromMilliseconds(500);

        /// <summary>Las de producción: el contenedor «firmas» y Nominatim.</summary>
        public static DependenciasFirmasReparto DeProduccion()
            => new DependenciasFirmasReparto { Almacen = new AlmacenFirmasEntregaAzure(), Geocodificador = new GeocodificadorInversoNominatim() };
    }

    public partial class ServicioRepartoPropio
    {
        public const string MENSAJE_FALTA_SCRIPT_FIRMAS = "Falta lanzar el script Scripts/Issue770_FirmasBlob.sql (firmas en el almacenamiento). Avise a Informática.";
        internal const int MAXIMO_LOTE_MIGRACION = 500;

        private readonly DependenciasFirmasReparto firmas;

        /// <summary>
        /// El texto que vio quien firma: el que manda la PDA (recortado a <see cref="EntregaRepartoDTO.MAXIMO_TEXTO_CONFORMIDAD"/>)
        /// o, si es una PDA vieja, el de la misma regla que Ariadna (<see cref="TextoConformidadEntrega"/>).
        /// </summary>
        internal static string TextoConformidad(EntregaRepartoDTO peticion, EnviosAgencia envio, bool recogida, short? bultosRecogidos,
            string cobro, decimal? importeCobrado)
        {
            string enviado = Limpio(peticion?.TextoConformidad);
            if (enviado != null)
            {
                return Recortar(enviado, EntregaRepartoDTO.MAXIMO_TEXTO_CONFORMIDAD);
            }
            int bultosEnvio = Math.Max((int)envio.Bultos, 1);
            return recogida
                ? TextoConformidadEntrega.Recogida(bultosRecogidos ?? bultosEnvio)
                : TextoConformidadEntrega.Entrega(bultosEnvio, envio.Pedido, TextoConformidadEntrega.FormaDe(cobro), importeCobrado);
        }

        /// <summary>La firma sellada, o null si no se ha podido (queda apuntado en ELMAH, sin datos personales).</summary>
        private async Task<byte[]> SellarFirma(byte[] firma, PruebaEntregaReparto prueba, string conformidad)
        {
            string direccion = prueba.Latitud.HasValue && prueba.Longitud.HasValue
                ? await DireccionDe(prueba.Latitud.Value, prueba.Longitud.Value, prueba.EnvioId).ConfigureAwait(false)
                : null;
            try
            {
                return SelloFirmaEntrega.Sellar(firma, new DatosSelloFirma
                {
                    TextoConformidad = conformidad,
                    FechaHora = prueba.FechaHora,
                    Receptor = prueba.Receptor,
                    DniReceptor = prueba.DniReceptor,
                    Latitud = prueba.Latitud,
                    Longitud = prueba.Longitud,
                    Direccion = direccion
                });
            }
            catch (Exception ex)
            {
                Apuntar($"[Reparto propio #770] No se pudo sellar la firma del envío {prueba.EnvioId}; se guarda sin sellar en la columna Firma ({ex.GetType().Name}).", ex);
                return null;
            }
        }

        private async Task<string> DireccionDe(decimal latitud, decimal longitud, int envio)
        {
            if (firmas.Geocodificador == null)
            {
                return null;
            }
            try
            {
                Task<string> busqueda = firmas.Geocodificador.Direccion(latitud, longitud);
                Task primera = await Task.WhenAny(busqueda, Task.Delay(firmas.TiempoMaximoDireccion)).ConfigureAwait(false);
                if (primera != busqueda)
                {
                    _ = busqueda.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    return null;
                }
                return await busqueda.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Sin dirección el sello lleva las coordenadas: no merece un error en ELMAH por cada corte de Nominatim.
                return null;
            }
        }

        /// <summary>
        /// Después de registrar: sube la firma al contenedor «firmas» y deja RutaBlob (Firma = NULL). Si falla, la firma sigue en
        /// la columna (la entrega ya está hecha) y la recoge la migración.
        /// </summary>
        private async Task GuardarFirmaEnBlob(PruebaEntregaReparto prueba, EnviosAgencia envio, byte[] firma)
        {
            if (prueba.Id <= 0 || firmas.Almacen == null || !firmas.Almacen.Configurado || !repositorio.TieneRutaBlob())
            {
                return;
            }
            string ruta = AlmacenFirmasEntregaAzure.Ruta(envio.Empresa, envio.Numero, prueba.Id, prueba.FechaHora);
            try
            {
                await firmas.Almacen.Subir(ruta, firma).ConfigureAwait(false);
                await repositorio.GuardarRutaBlob(prueba.Id, ruta).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Apuntar($"[Reparto propio #770] No se pudo guardar en el almacenamiento la firma de la prueba {prueba.Id} (envío {envio.Numero}); " +
                    "queda en la columna Firma hasta la migración.", ex);
            }
        }

        public async Task<ResultadoReparto<byte[]>> Firma(int envioId)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<byte[]>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            FirmaGuardadaReparto guardada = await repositorio.LeerFirma(envioId).ConfigureAwait(false);
            if (guardada == null)
            {
                return ResultadoReparto<byte[]>.Vacio();
            }
            if (!string.IsNullOrWhiteSpace(guardada.RutaBlob))
            {
                if (firmas.Almacen != null && firmas.Almacen.Configurado)
                {
                    try
                    {
                        byte[] png = await firmas.Almacen.Descargar(guardada.RutaBlob.Trim()).ConfigureAwait(false);
                        if (png != null && png.Length > 0)
                        {
                            return ResultadoReparto<byte[]>.Bien(png);
                        }
                    }
                    catch (Exception ex)
                    {
                        Apuntar($"[Reparto propio #770] No se pudo leer del almacenamiento la firma de la prueba {guardada.Id} (envío {envioId}).", ex);
                    }
                }
                if (guardada.Firma == null || guardada.Firma.Length == 0)
                {
                    return ResultadoReparto<byte[]>.Mal(TipoResultadoReparto.Invalido,
                        $"No se ha podido leer la firma del envío {envioId} del almacenamiento. Vuelva a intentarlo en un rato.");
                }
            }
            return guardada.Firma == null || guardada.Firma.Length == 0 ? ResultadoReparto<byte[]>.Vacio() : ResultadoReparto<byte[]>.Bien(guardada.Firma);
        }

        /// <summary>
        /// NestoAPI#770: las firmas que siguen en la columna (las de antes de #770 y las que no se pudieron subir) al contenedor
        /// «firmas», como están (las viejas no se sellan: no se vio ningún texto al firmarlas). Con <paramref name="aplicar"/> = false
        /// solo cuenta y lista; con true sube cada una, deja RutaBlob y pone Firma = NULL. Idempotente: la ruta sale de la fila,
        /// así que repetir sobrescribe el mismo blob.
        /// </summary>
        public async Task<ResultadoReparto<ResultadoMigracionFirmasDTO>> MigrarFirmas(bool aplicar, int maximo)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<ResultadoMigracionFirmasDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            if (!repositorio.TieneRutaBlob())
            {
                return ResultadoReparto<ResultadoMigracionFirmasDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_FIRMAS);
            }
            if (aplicar && (firmas.Almacen == null || !firmas.Almacen.Configurado))
            {
                return ResultadoReparto<ResultadoMigracionFirmasDTO>.Mal(TipoResultadoReparto.Invalido,
                    "No está configurado el almacenamiento de las firmas (Almacen:FotosBultosConexion).");
            }
            int lote = Math.Min(Math.Max(maximo, 1), MAXIMO_LOTE_MIGRACION);
            var resultado = new ResultadoMigracionFirmasDTO
            {
                Aplicado = aplicar,
                Pendientes = await repositorio.ContarFirmasEnColumna().ConfigureAwait(false)
            };
            List<FirmaGuardadaReparto> filas = await repositorio.LeerFirmasEnColumna(lote).ConfigureAwait(false) ?? new List<FirmaGuardadaReparto>();
            foreach (FirmaGuardadaReparto fila in filas)
            {
                string ruta = AlmacenFirmasEntregaAzure.Ruta(fila.Empresa, fila.EnvioId, fila.Id, fila.FechaHora);
                var dto = new FirmaMigradaDTO { Id = fila.Id, EnvioId = fila.EnvioId, FechaHora = fila.FechaHora, Bytes = fila.Bytes, RutaBlob = ruta };
                resultado.Firmas.Add(dto);
                if (!aplicar || fila.Firma == null || fila.Firma.Length == 0)
                {
                    continue;
                }
                try
                {
                    await firmas.Almacen.Subir(ruta, fila.Firma).ConfigureAwait(false);
                    await repositorio.GuardarRutaBlob(fila.Id, ruta).ConfigureAwait(false);
                    resultado.Migradas++;
                }
                catch (Exception ex)
                {
                    resultado.Errores++;
                    dto.Error = ex.Message;
                    Apuntar($"[Reparto propio #770] No se pudo migrar la firma de la prueba {fila.Id} (envío {fila.EnvioId}).", ex);
                }
            }
            return ResultadoReparto<ResultadoMigracionFirmasDTO>.Bien(resultado);
        }

        private void Apuntar(string mensaje, Exception ex)
        {
            try
            {
                firmas.RegistrarError?.Invoke(new Exception(mensaje + " " + ex?.Message, ex));
            }
            catch
            {
                // Apuntar un fallo nunca rompe la entrega.
            }
        }
    }
}
