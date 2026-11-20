using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#881: el centro de cada código postal (dbo.CentrosCodigosPostales, GeoNames): para las paradas sin coordenadas.</summary>
    public interface ICentrosCodigosPostales
    {
        /// <summary>Los que haya de esos códigos postales. Nunca lanza: sin tabla o con error, vacío.</summary>
        Task<Dictionary<string, CoordenadasGeo>> Leer(IReadOnlyCollection<string> codigosPostales);
    }

    public class CentrosCodigosPostalesSql : ICentrosCodigosPostales
    {
        public const string TABLA = "CentrosCodigosPostales";
        private readonly NVEntities db;

        public CentrosCodigosPostalesSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private class Fila
        {
            public string CodigoPostal { get; set; }
            public decimal Latitud { get; set; }
            public decimal Longitud { get; set; }
        }

        public async Task<Dictionary<string, CoordenadasGeo>> Leer(IReadOnlyCollection<string> codigosPostales)
        {
            var resultado = new Dictionary<string, CoordenadasGeo>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = (codigosPostales ?? new string[0]).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToList();
            if (lista.Count == 0 || !ColumnasOpcionales.ExisteTabla(db, TABLA))
            {
                return resultado;
            }
            try
            {
                object[] parametros = lista.Cast<object>().ToArray();
                string enLista = string.Join(", ", lista.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)));
                List<Fila> filas = await db.Database.SqlQuery<Fila>(
                    $"SELECT RTRIM(CodigoPostal) AS CodigoPostal, CAST(Latitud AS decimal(9,6)) AS Latitud, CAST(Longitud AS decimal(9,6)) AS Longitud " +
                    $"FROM dbo.{TABLA} WITH (NOLOCK) WHERE CodigoPostal IN ({enLista})", parametros).ToListAsync().ConfigureAwait(false);
                foreach (Fila fila in filas.Where(f => CoordenadasGeo.Validas(f.Latitud, f.Longitud)))
                {
                    resultado[fila.CodigoPostal] = new CoordenadasGeo(fila.Latitud, fila.Longitud) { PorCodigoPostal = true };
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] No se pudieron leer los centros de los códigos postales: {ex.Message}", ex));
            }
            return resultado;
        }
    }

    /// <summary>
    /// NestoAPI#881 (corte 1): el fin previsto de la ruta del repartidor. POST api/RepartoPropio/Estimacion evalúa el orden VIGENTE de sus
    /// paradas pendientes (no reordena ni guarda nada) y POST OrdenSugerido lo añade a su respuesta con el orden nuevo. Nunca falla: sin
    /// coordenadas, el centro del código postal; sin motor de rutas, la línea recta × 1,3 a 30 km/h (y sale «Aproximada»).
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        /// <summary>Un destino a menos de esto de Algete es Algete (el repartidor ha elegido «aquí» estando en el almacén).</summary>
        internal const double METROS_DESTINO_ES_ALGETE = 300;

        private ConstantesTiemposReparto ConstantesTiempos()
        {
            try
            {
                return paradas.ConfiguracionTiempos?.Leer() ?? ConstantesTiemposReparto.PorDefecto();
            }
            catch (Exception)
            {
                return ConstantesTiemposReparto.PorDefecto();
            }
        }

        /// <summary>El inicio y el final del recorrido de una petición (o el mensaje del 400). Lo comparten OrdenSugerido y Estimacion.</summary>
        private static string LeerExtremos(PeticionOrdenSugeridoDTO peticion, out CoordenadasGeo inicio, out CoordenadasGeo final, out bool conDestino)
        {
            inicio = null;
            final = SedesReparto.ALGETE;
            conDestino = false;
            bool conPosicion = peticion.Latitud.HasValue || peticion.Longitud.HasValue;
            if (conPosicion && !CoordenadasGeo.Validas(peticion.Latitud, peticion.Longitud)
                && !(peticion.Latitud == 0m && peticion.Longitud == 0m))
            {
                return "La posición no es válida: mande Latitud y Longitud juntas, en grados, o ninguna de las dos.";
            }
            conDestino = peticion.DestinoLatitud.HasValue || peticion.DestinoLongitud.HasValue;
            if (conDestino && !CoordenadasGeo.Validas(peticion.DestinoLatitud, peticion.DestinoLongitud))
            {
                return "El destino no es válido: mande DestinoLatitud y DestinoLongitud juntas, en grados, o ninguna de las dos para acabar en Algete.";
            }
            // La casa del repartidor (u otro punto) no se guarda: solo se usa para este cálculo.
            final = conDestino ? new CoordenadasGeo(peticion.DestinoLatitud.Value, peticion.DestinoLongitud.Value) : SedesReparto.ALGETE;
            // 0,0 es lo que manda un GPS sin señal: como si no viniera.
            inicio = CoordenadasGeo.Validas(peticion.Latitud, peticion.Longitud)
                ? new CoordenadasGeo(peticion.Latitud.Value, peticion.Longitud.Value)
                : null;
            return null;
        }

        /// <summary>
        /// POST api/RepartoPropio/Estimacion: con la MISMA petición que OrdenSugerido. Solo las paradas PENDIENTES (Tramitado) del que llama,
        /// en su orden vigente (el de GET Envios). No reordena ni guarda el orden.
        /// </summary>
        public async Task<ResultadoReparto<EstimacionFinRutaDTO>> Estimacion(PeticionOrdenSugeridoDTO peticion, string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return ResultadoReparto<EstimacionFinRutaDTO>.Mal(TipoResultadoReparto.Invalido, "No se sabe quién pide la estimación.");
            }
            peticion = peticion ?? new PeticionOrdenSugeridoDTO();
            string error = LeerExtremos(peticion, out CoordenadasGeo inicio, out CoordenadasGeo final, out bool conDestino);
            if (error != null)
            {
                return ResultadoReparto<EstimacionFinRutaDTO>.Mal(TipoResultadoReparto.Invalido, error);
            }
            ResultadoReparto<List<EnvioRepartoDTO>> lista = await Envios(peticion.Fecha, soloMios: true, usuario: usuario, soloCargados: peticion.SoloCargados).ConfigureAwait(false);
            if (lista.Tipo != TipoResultadoReparto.Ok)
            {
                return new ResultadoReparto<EstimacionFinRutaDTO> { Tipo = lista.Tipo, Mensaje = lista.Mensaje, Codigo = lista.Codigo, Detalles = lista.Detalles };
            }
            List<EnvioRepartoDTO> pendientes = (lista.Contenido ?? new List<EnvioRepartoDTO>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Repartidor) && MismoUsuario(p.Repartidor, usuario) && p.Estado == EnvioRepartoDTO.ESTADO_TRAMITADO)
                .ToList();
            DateTime ahoraPda = peticion.FechaHora ?? ahora();
            DateTime horaSalida = HoraSalida((peticion.Fecha ?? ahoraPda).Date, ahoraPda) ?? ahoraPda;
            EstimacionFinRutaDTO estimacion = await EstimarFin(pendientes, inicio ?? SedesReparto.ALGETE, final, conDestino, horaSalida, usuario).ConfigureAwait(false);
            // NestoAPI#881 (corte 3): la foto de la previsión (una cada 10 min por repartidor). Nunca la rompe.
            await FotoEstimacion(estimacion, usuario, inicio != null ? OrdenSugeridoRepartoDTO.ORIGEN_POSICION : OrdenSugeridoRepartoDTO.ORIGEN_ALMACEN,
                FotoEstimacionReparto.CALCULO_ESTIMACION).ConfigureAwait(false);
            return ResultadoReparto<EstimacionFinRutaDTO>.Bien(estimacion);
        }

        /// <summary>
        /// El fin previsto de <paramref name="pendientes"/> en ese orden, saliendo de <paramref name="salida"/> a <paramref name="horaSalida"/>
        /// y acabando en <paramref name="final"/>. Nunca lanza.
        /// </summary>
        internal async Task<EstimacionFinRutaDTO> EstimarFin(IReadOnlyList<EnvioRepartoDTO> pendientes, CoordenadasGeo salida, CoordenadasGeo final,
            bool destinoPersonalizado, DateTime horaSalida, string usuario)
        {
            ConstantesTiemposReparto constantes = ConstantesTiempos();
            List<EnvioRepartoDTO> lista = (pendientes ?? new List<EnvioRepartoDTO>()).ToList();
            bool aproximada = false;

            // 1. Dónde está cada parada: sus coordenadas, si no el centro de su código postal; sin nada, solo cuentan sus minutos.
            Dictionary<string, CoordenadasGeo> centros = await CentrosDe(lista.Where(p => !CoordenadasGeo.Validas(p.Latitud, p.Longitud))).ConfigureAwait(false);
            var posiciones = new List<CoordenadasGeo>();
            var aproximadas = new List<bool>();
            foreach (EnvioRepartoDTO parada in lista)
            {
                if (CoordenadasGeo.Validas(parada.Latitud, parada.Longitud))
                {
                    posiciones.Add(new CoordenadasGeo(parada.Latitud.Value, parada.Longitud.Value));
                    aproximadas.Add(parada.CoordenadasAproximadas);
                }
                else if (!string.IsNullOrWhiteSpace(parada.CodigoPostal) && centros.TryGetValue(parada.CodigoPostal.Trim(), out CoordenadasGeo centro))
                {
                    posiciones.Add(centro);
                    aproximadas.Add(true);
                }
                else
                {
                    posiciones.Add(null);
                    aproximadas.Add(true);
                }
            }
            aproximada |= aproximadas.Any(a => a);

            // 2. ¿Pasa por Algete? Si acaba allí, sí; si acaba en otro sitio, solo si lleva algo que dejar en Algete.
            bool destinoEsAlgete = !destinoPersonalizado || HeuristicaRutas.DistanciaKm(final, SedesReparto.ALGETE) * 1000 <= METROS_DESTINO_ES_ALGETE;
            bool pasaPorAlgete = destinoEsAlgete || await LlevaAlgoParaAlgete(lista, usuario).ConfigureAwait(false);
            bool desvioPorAlgete = pasaPorAlgete && !destinoEsAlgete;

            // 3. Los puntos con posición, en orden: salida, paradas, (Algete), final.
            var puntos = new List<CoordenadasGeo> { salida };
            var indicePunto = new List<int>(); // de cada parada, su punto (−1 sin posición)
            foreach (CoordenadasGeo posicion in posiciones)
            {
                if (posicion == null)
                {
                    indicePunto.Add(-1);
                    continue;
                }
                indicePunto.Add(puntos.Count);
                puntos.Add(posicion);
            }
            int puntoFinal;
            int? puntoDestino = null;
            if (desvioPorAlgete)
            {
                puntoFinal = puntos.Count;
                puntos.Add(SedesReparto.ALGETE);
                puntoDestino = puntos.Count;
                puntos.Add(final);
            }
            else
            {
                puntoFinal = puntos.Count;
                puntos.Add(destinoEsAlgete ? SedesReparto.ALGETE : final);
            }

            MatrizTiempos matriz = await MatrizPara(puntos).ConfigureAwait(false);
            aproximada |= matriz.Motor != OrdenSugeridoRepartoDTO.MOTOR_OSRM;

            // 4. Un tramo por parada (desde el último punto con posición; sin posición, 0) y el de la vuelta.
            var minutosTramos = new List<double>();
            var kmTramos = new List<double>();
            int anterior = 0;
            for (int i = 0; i < lista.Count; i++)
            {
                int punto = indicePunto[i];
                if (punto < 0)
                {
                    minutosTramos.Add(0);
                    kmTramos.Add(0);
                    continue;
                }
                minutosTramos.Add(matriz.Minutos[anterior, punto]);
                kmTramos.Add(KmEntre(matriz, puntos, anterior, punto));
                anterior = punto;
            }
            minutosTramos.Add(matriz.Minutos[anterior, puntoFinal]);
            kmTramos.Add(KmEntre(matriz, puntos, anterior, puntoFinal));

            List<double> minutosParada = MinutosConModeloSiManda(lista, MinutosParadaReparto.Recorrido(lista, constantes), constantes, usuario, horaSalida,
                out bool minutosDelModelo);
            DateTime? cierre = HorarioCierreReparto.De(horaSalida.Date, constantes, paradas.EsDiaSinReparto);
            ResultadoFinRuta calculo = CalculoFinRuta.Calcular(new EntradaFinRuta
            {
                Salida = horaSalida,
                MinutosTramos = minutosTramos,
                KmTramos = kmTramos,
                MinutosParada = minutosParada,
                FactorConduccion = constantes.FactorConduccion,
                PasaPorAlgete = pasaPorAlgete,
                MinutosDescargaAlgete = constantes.MinutosDescargaAlgete,
                MinutosAlgeteADestino = puntoDestino.HasValue ? matriz.Minutos[puntoFinal, puntoDestino.Value] : (double?)null,
                KmAlgeteADestino = puntoDestino.HasValue ? KmEntre(matriz, puntos, puntoFinal, puntoDestino.Value) : (double?)null,
                HoraCierre = cierre
            });

            var estimacion = new EstimacionFinRutaDTO
            {
                FinEstimado = calculo.FinEstimado,
                LlegadaDestino = calculo.LlegadaDestino,
                MinutosConduccion = Entero(calculo.MinutosConduccion),
                MinutosParadas = Entero(calculo.MinutosParadas),
                MinutosDescarga = Entero(calculo.MinutosDescarga),
                Km = Math.Round((decimal)calculo.Km, 1),
                HoraCierre = cierre,
                HoraCierreDelDia = cierre.HasValue ? cierre.Value.ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture) : null,
                CalculadoEl = horaSalida,
                MinutosRestantes = Math.Max(0, Entero((calculo.FinEstimado - horaSalida).TotalMinutes)),
                SeComparaConCierre = calculo.SeComparaConCierre,
                PasaDelCierre = calculo.PasaDelCierre,
                MinutosDeMas = calculo.MinutosDeMas,
                Aviso = TextosFinRuta.Aviso(calculo.FinEstimado, calculo.PasaDelCierre, calculo.MinutosDeMas),
                Aproximada = aproximada,
                Motor = matriz.Motor,
                Destino = destinoPersonalizado ? EstimacionFinRutaDTO.DESTINO_PERSONALIZADO : EstimacionFinRutaDTO.DESTINO_ALMACEN,
                PasaPorAlgete = pasaPorAlgete,
                HoraSalida = horaSalida,
                NumeroParadas = lista.Count,
                MinutosDe = minutosDelModelo ? ConstantesTiemposReparto.QUIEN_MANDA_MODELO : ConstantesTiemposReparto.QUIEN_MANDA_REGLA
            };
            for (int i = 0; i < lista.Count; i++)
            {
                estimacion.Paradas.Add(new ParadaEstimadaDTO
                {
                    EnvioId = lista[i].EnvioId,
                    Orden = i + 1,
                    LlegadaEstimada = calculo.Llegadas[i],
                    MinutosParada = Entero(minutosParada[i]),
                    Aproximada = aproximadas[i]
                });
            }
            return estimacion;
        }

        private static int Entero(double minutos) => (int)Math.Round(minutos, MidpointRounding.AwayFromZero);

        private static double KmEntre(MatrizTiempos matriz, IReadOnlyList<CoordenadasGeo> puntos, int de, int a)
            => matriz.Kilometros != null ? matriz.Kilometros[de, a] : HeuristicaRutas.DistanciaKm(puntos[de], puntos[a]) * HeuristicaRutas.FACTOR_CARRETERA;

        /// <summary>La matriz de OSRM /table o, si no contesta (o no hay motor), la línea recta. Nunca lanza.</summary>
        private async Task<MatrizTiempos> MatrizPara(IReadOnlyList<CoordenadasGeo> puntos)
        {
            MatrizTiempos matriz = null;
            if (paradas.Tiempos != null && puntos.Count >= 2)
            {
                try
                {
                    matriz = await paradas.Tiempos.Calcular(puntos).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto #881] La matriz de tiempos ha fallado; el fin de ruta se estima en línea recta. {ex.Message}", ex));
                }
            }
            return matriz != null && matriz.Puntos == puntos.Count ? matriz : MatrizTiempos.Heuristica(puntos);
        }

        private async Task<Dictionary<string, CoordenadasGeo>> CentrosDe(IEnumerable<EnvioRepartoDTO> sinCoordenadas)
        {
            List<string> codigos = sinCoordenadas.Select(p => p.CodigoPostal?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            if (codigos.Count == 0 || paradas.Centros == null)
            {
                return new Dictionary<string, CoordenadasGeo>(StringComparer.OrdinalIgnoreCase);
            }
            try
            {
                return await paradas.Centros.Leer(codigos).ConfigureAwait(false) ?? new Dictionary<string, CoordenadasGeo>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] No se pudieron leer los centros de los códigos postales: {ex.Message}", ex));
                return new Dictionary<string, CoordenadasGeo>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Lleva (o va a llevar) algo que dejar en Algete: una recogida pendiente en su ruta, o bultos recogidos (NR de una recogida o R de
        /// una reposición que vuelve) en su furgoneta. La caja con efectivo no se mira (exige leer el extracto de la caja: no es barato).
        /// </summary>
        internal async Task<bool> LlevaAlgoParaAlgete(IEnumerable<EnvioRepartoDTO> pendientes, string usuario)
        {
            if ((pendientes ?? Enumerable.Empty<EnvioRepartoDTO>()).Any(p => p.TipoParada == TiposParadaReparto.NOTA_RECOGIDA))
            {
                return true;
            }
            if (!FurgonetaDisponible() || string.IsNullOrWhiteSpace(usuario))
            {
                return false;
            }
            try
            {
                return (await EnFurgoneta(usuario).ConfigureAwait(false))
                    .Any(m => m.Tipo == TiposMovimientoBulto.RECOGIDO && (m.TipoCodigo == CodigoBultoFurgoneta.NR || m.TipoCodigo == CodigoBultoFurgoneta.R));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] No se pudo mirar la furgoneta para el fin de ruta: {ex.Message}", ex));
                return false;
            }
        }
    }
}
