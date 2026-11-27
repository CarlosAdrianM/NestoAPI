using NestoAPI.Infraestructure.Inmovilizados;
using NestoAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Aparatos
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#913, corte 2 (Carlos 27/11/26): INMOVILIZADOS. Enlazar cada aparato (producto + serie) con su ficha de inmovilizado
    // sin que nadie lo teclee: la propuesta (solo lectura), el enlace (con ensayo) y los descuadres nuevos. El enlace vive donde
    // siempre: CabAlquileres.Inmovilizado (FK a Inmovilizados por Empresa + Número), lo mismo que escribe la ventana de Alquileres.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Lo leído de un inmovilizado: la serie (tal cual, limpia) y, si la hay, la referencia del producto.</summary>
    public class SerieLeidaInmovilizado
    {
        /// <summary>Lo que va detrás de la etiqueta de serie, sin puntuación al final («12345 (demo)»).</summary>
        public string Serie { get; set; }
        /// <summary>El primer tramo de la serie, si detrás hay más texto («12345»); si no, igual que <see cref="Serie"/>.</summary>
        public string PrimerTramo { get; set; }
        public string Referencia { get; set; }

        /// <summary>Las formas a probar, en orden: la serie entera y su primer tramo (sin repetir).</summary>
        public List<string> Candidatas()
        {
            var lista = new List<string>();
            foreach (string s in new[] { Serie, PrimerTramo })
            {
                if (!string.IsNullOrWhiteSpace(s) && !lista.Any(x => ReglaSituacionAparato.Clave(x) == ReglaSituacionAparato.Clave(s)))
                {
                    lista.Add(s.Trim());
                }
            }
            return lista;
        }
    }

    /// <summary>
    /// NestoAPI#913 corte 2: LA REGLA (pura) de la serie escrita en la descripción de un inmovilizado. Primero la de la etiqueta
    /// (<see cref="ReglaDescripcionEtiquetaInmovilizado.SerieEscrita"/>: «n/s», «s/n», «nº serie», «nº de serie», «no. serie»); si no
    /// casa, una más amplia: la etiqueta pegada a la serie («n/s12345», «S/N:AB-1»), «número de serie», «núm. serie», «num serie».
    /// Quita los «:», «#», «=» o «-» de delante y la puntuación del final. «Serie» a secas NO es una serie (puede ser el modelo).
    /// </summary>
    public static class ReglaSerieInmovilizado
    {
        private static readonly Regex amplia = new Regex(
            @"(?<![\p{L}\p{N}])(n\s*/\s*s|s\s*/\s*n|n[uú]m(ero)?\.?\s*(de\s+)?serie|n[º°o]?\.?\s*(de\s+)?serie)\s*[:.#=\-]?\s*(?<valor>.*)$",
            RegexOptions.IgnoreCase);

        private static readonly char[] DELANTE = { ':', '#', '=', '-', '.', ' ' };
        private static readonly char[] DETRAS = { '.', ',', ';', ':', ' ' };

        public static SerieLeidaInmovilizado Leer(string descripcion)
        {
            string serie = ReglaDescripcionEtiquetaInmovilizado.SerieEscrita(descripcion);
            if (serie == null)
            {
                string texto = string.Join(" ", (descripcion ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                Match m = amplia.Match(texto);
                serie = m.Success ? m.Groups["valor"].Value : null;
            }
            serie = Limpiar(serie);
            if (serie == null)
            {
                return null;
            }
            string primero = Limpiar(serie.Split(new[] { ' ', '(', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
            return new SerieLeidaInmovilizado
            {
                Serie = serie,
                PrimerTramo = primero ?? serie,
                Referencia = ReglaDescripcionEtiquetaInmovilizado.ReferenciaEscrita(descripcion)
            };
        }

        internal static string Limpiar(string valor)
        {
            string limpio = (valor ?? string.Empty).Trim().TrimStart(DELANTE).TrimEnd(DETRAS).Trim();
            if (limpio.EndsWith(")", StringComparison.Ordinal) && !limpio.Contains("("))
            {
                limpio = limpio.TrimEnd(')').TrimEnd(DETRAS).Trim();
            }
            return limpio.Length == 0 ? null : limpio;
        }

        /// <summary>
        /// La serie y el producto de una ficha: las columnas (si el script Issue913b está aplicado y están rellenas) mandan; si no,
        /// lo escrito en la descripción. Null si no hay serie.
        /// </summary>
        public static SerieLeidaInmovilizado DeFicha(InmovilizadoAparatoFila fila)
        {
            if (fila == null)
            {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(fila.NumeroSerie))
            {
                return new SerieLeidaInmovilizado
                {
                    Serie = fila.NumeroSerie.Trim(),
                    PrimerTramo = fila.NumeroSerie.Trim(),
                    Referencia = string.IsNullOrWhiteSpace(fila.Producto) ? null : fila.Producto.Trim()
                };
            }
            SerieLeidaInmovilizado leida = Leer(fila.Descripcion);
            if (leida != null && leida.Referencia == null && !string.IsNullOrWhiteSpace(fila.Producto))
            {
                leida.Referencia = fila.Producto.Trim();
            }
            return leida;
        }

        /// <summary>De dónde sale la serie: «Columna» (Inmovilizados.NumeroSerie) o «Texto» (la descripción).</summary>
        public static string Origen(InmovilizadoAparatoFila fila) => string.IsNullOrWhiteSpace(fila?.NumeroSerie) ? "Texto" : "Columna";
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CertezaEnlaceInmovilizado
    {
        /// <summary>Un solo aparato con esa serie exacta (y la referencia, si está escrita).</summary>
        UnaCoincidenciaExacta,
        /// <summary>Más de un aparato (de distintos productos) con esa serie exacta.</summary>
        VariasCoincidencias,
        /// <summary>Ninguna exacta; sí alguna escrita de otra forma, cortada o con la referencia escrita distinta.</summary>
        SoloPorParecido,
        Ninguna
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MotivoAparatoSinInmovilizado
    {
        /// <summary>El registro pone un inmovilizado que no existe.</summary>
        FichaNoExiste,
        /// <summary>El registro pone un inmovilizado anulado.</summary>
        FichaAnulada,
        /// <summary>El registro pone un inmovilizado cuya serie (escrita o en la columna) es otra.</summary>
        FichaConOtraSerie
    }

    public class CandidatoEnlaceInmovilizadoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string NumeroSerie { get; set; }
        /// <summary>Con la serie leída exacta (recortada y sin mayúsculas) y, si está escrita, la misma referencia.</summary>
        public bool Exacta { get; set; }
        /// <summary>Los registros de Alquileres de ese aparato (vacía si no tiene).</summary>
        public List<int> Registros { get; set; } = new List<int>();
        public SituacionAparato SituacionCalculada { get; set; }
        /// <summary>Se puede enlazar (un solo registro, de la misma empresa y sin otro inmovilizado).</summary>
        public bool Aplicable { get; set; }
        /// <summary>Por qué no se puede (de usted); null si se puede.</summary>
        public string Motivo { get; set; }
    }

    public class PropuestaInmovilizadoDTO
    {
        public string Empresa { get; set; }
        /// <summary>El número tal cual («1824»).</summary>
        public string Inmovilizado { get; set; }
        /// <summary>«I/1824».</summary>
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public string SerieLeida { get; set; }
        public string ReferenciaLeida { get; set; }
        /// <summary>«Texto» (de la descripción) o «Columna» (Inmovilizados.NumeroSerie, con el script Issue913b).</summary>
        public string OrigenSerie { get; set; }
        public CertezaEnlaceInmovilizado Certeza { get; set; }
        /// <summary>Una sola coincidencia exacta y se puede enlazar sin más: es lo que enlaza el bloque.</summary>
        public bool CertezaAlta { get; set; }
        public string TextoCerteza { get; set; }
        public List<CandidatoEnlaceInmovilizadoDTO> Candidatos { get; set; } = new List<CandidatoEnlaceInmovilizadoDTO>();
        /// <summary>Con certeza alta: el registro que se enlazaría.</summary>
        public int? RegistroPropuesto { get; set; }
        /// <summary>Por qué no es de certeza alta (de usted); null si lo es.</summary>
        public string MotivoNoAplicable { get; set; }

        [JsonIgnore] internal string ProductoPropuesto { get; set; }
        [JsonIgnore] internal string SeriePropuesta { get; set; }
    }

    public class AparatoSinInmovilizadoDTO
    {
        public string Empresa { get; set; }
        public int Registro { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string NumeroSerie { get; set; }
        /// <summary>Lo que pone el registro («1700»).</summary>
        public string InmovilizadoDelRegistro { get; set; }
        public MotivoAparatoSinInmovilizado Motivo { get; set; }
        public string Texto { get; set; }
        /// <summary>Un inmovilizado vivo y sin enlazar que nombra esta serie («I/1824»), si lo hay.</summary>
        public string InmovilizadoPropuesto { get; set; }
    }

    public class PropuestasInmovilizadosDTO
    {
        public int TotalInmovilizados { get; set; }
        /// <summary>Con algún registro de Alquileres que los pone.</summary>
        public int Enlazados { get; set; }
        public int Anulados { get; set; }
        /// <summary>Vivos, sin enlazar y con serie leída: los de <see cref="Propuestas"/>.</summary>
        public int SinEnlazarConSerie { get; set; }
        /// <summary>Vivos, sin enlazar y sin serie (mobiliario, informática…): no se proponen.</summary>
        public int SinEnlazarSinSerie { get; set; }
        public int ConCertezaAlta { get; set; }
        /// <summary>El script Issue913b está aplicado: Inmovilizados tiene Producto y NumeroSerie.</summary>
        public bool ColumnasEnInmovilizados { get; set; }
        public List<PropuestaInmovilizadoDTO> Propuestas { get; set; } = new List<PropuestaInmovilizadoDTO>();
        public List<AparatoSinInmovilizadoDTO> AparatosSinInmovilizado { get; set; } = new List<AparatoSinInmovilizadoDTO>();
    }

    /// <summary>
    /// NestoAPI#913 corte 2: LA REGLA (pura) de las propuestas de enlace. Aparatos conocidos = producto + serie de los registros de
    /// Alquileres, las compras, las ventas y NumerosSerie (un inmovilizado solo no hace aparato). Para cada inmovilizado vivo, sin
    /// enlazar y con serie: exacta (la entera y, si no, su primer tramo; con referencia escrita, de ese producto); si no, parecida
    /// (<see cref="ReglaSeriesParecidas.Normalizar"/>, la referencia escrita distinta o, con la descripción llena —50 caracteres—,
    /// cortada). Certeza alta = una sola exacta, con un solo registro, de la misma empresa, sin otro inmovilizado y que ningún otro
    /// inmovilizado proponga el mismo registro.
    /// </summary>
    public static class ReglaPropuestasInmovilizados
    {
        /// <summary>Inmovilizados.Descripción es char(50): con 50 caracteres puede venir cortada.</summary>
        public const int LONGITUD_DESCRIPCION = 50;

        private class Aparato
        {
            internal string Producto;
            internal string Serie;
            internal string Clave => ReglaSituacionAparato.Clave(Producto) + "|" + ReglaSituacionAparato.Clave(Serie);
        }

        internal static string ClaveInmovilizado(string empresa, string numero)
            => ReglaSituacionAparato.Clave(empresa) + "|" + (CodigoInmovilizado.Normalizar(numero) ?? string.Empty).ToUpperInvariant();

        public static PropuestasInmovilizadosDTO Calcular(DatosAparatos datos)
        {
            datos = datos ?? new DatosAparatos();
            List<InmovilizadoAparatoFila> inmovilizados = datos.Inmovilizados ?? new List<InmovilizadoAparatoFila>();
            List<RegistroAparatoDTO> registros = datos.Registros ?? new List<RegistroAparatoDTO>();
            Dictionary<string, string> nombres = (datos.Productos ?? new List<ProductoAparatoFila>())
                .GroupBy(p => ReglaSituacionAparato.Clave(p.Producto))
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim(), StringComparer.Ordinal);

            // Los aparatos conocidos (sin los inmovilizados) y sus movimientos, para la situación de cada candidato
            List<MovimientoAparatoDTO> movimientos = ConstructorMovimientosAparato.Movimientos(new DatosAparatos
            {
                Productos = datos.Productos,
                Registros = datos.Registros,
                Ventas = datos.Ventas,
                Compras = datos.Compras
            });
            var aparatos = new Dictionary<string, Aparato>(StringComparer.Ordinal);
            void Conocer(string producto, string serie)
            {
                if (ReglaSituacionAparato.Clave(producto).Length == 0 || ReglaSituacionAparato.Clave(serie).Length == 0)
                {
                    return;
                }
                var a = new Aparato { Producto = producto.Trim(), Serie = serie.Trim() };
                if (!aparatos.ContainsKey(a.Clave))
                {
                    aparatos[a.Clave] = a;
                }
            }
            foreach (MovimientoAparatoDTO m in movimientos)
            {
                Conocer(m.Producto, m.NumeroSerie);
            }
            foreach (NumeroSerieFila n in datos.NumerosSerie ?? new List<NumeroSerieFila>())
            {
                Conocer(n.Producto, n.NumeroSerie);
            }
            ILookup<string, Aparato> porSerie = aparatos.Values.ToLookup(a => ReglaSituacionAparato.Clave(a.Serie));
            ILookup<string, Aparato> porForma = aparatos.Values.ToLookup(a => ReglaSeriesParecidas.Normalizar(a.Serie));
            ILookup<string, MovimientoAparatoDTO> movimientosPorAparato = movimientos
                .ToLookup(m => ReglaSituacionAparato.Clave(m.Producto) + "|" + ReglaSituacionAparato.Clave(m.NumeroSerie));
            ILookup<string, RegistroAparatoDTO> registrosPorAparato = registros
                .ToLookup(r => ReglaSituacionAparato.Clave(r.Producto) + "|" + ReglaSituacionAparato.Clave(r.NumeroSerie));

            // Qué inmovilizado pone cada registro
            ILookup<string, RegistroAparatoDTO> registrosPorInmovilizado = registros
                .Where(r => !string.IsNullOrWhiteSpace(r.Inmovilizado))
                .ToLookup(r => ClaveInmovilizado(r.Empresa, r.Inmovilizado));
            Dictionary<string, InmovilizadoAparatoFila> fichas = inmovilizados
                .GroupBy(i => ClaveInmovilizado(i.Empresa, i.Numero))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var resultado = new PropuestasInmovilizadosDTO
            {
                TotalInmovilizados = inmovilizados.Count,
                ColumnasEnInmovilizados = datos.ColumnasAparatoEnInmovilizados
            };

            foreach (InmovilizadoAparatoFila ficha in inmovilizados
                .OrderBy(i => i.Empresa?.Trim(), StringComparer.Ordinal)
                .ThenByDescending(i => i.FechaCreacion ?? DateTime.MinValue)
                .ThenBy(i => i.Numero?.Trim(), StringComparer.Ordinal))
            {
                if (registrosPorInmovilizado[ClaveInmovilizado(ficha.Empresa, ficha.Numero)].Any())
                {
                    resultado.Enlazados++;
                    continue;
                }
                if (ficha.Estado < 0)
                {
                    resultado.Anulados++;
                    continue;
                }
                SerieLeidaInmovilizado leida = ReglaSerieInmovilizado.DeFicha(ficha);
                if (leida == null)
                {
                    resultado.SinEnlazarSinSerie++;
                    continue;
                }
                resultado.SinEnlazarConSerie++;
                resultado.Propuestas.Add(Proponer(ficha, leida, porSerie, porForma, registrosPorAparato, movimientosPorAparato, nombres));
            }

            // Dos inmovilizados que proponen el mismo registro: ninguno es de certeza alta
            foreach (IGrouping<int, PropuestaInmovilizadoDTO> repetidos in resultado.Propuestas
                .Where(p => p.CertezaAlta && p.RegistroPropuesto.HasValue)
                .GroupBy(p => p.RegistroPropuesto.Value)
                .Where(g => g.Count() > 1)
                .ToList())
            {
                foreach (PropuestaInmovilizadoDTO p in repetidos)
                {
                    p.CertezaAlta = false;
                    p.MotivoNoAplicable = $"Hay {repetidos.Count()} inmovilizados ({string.Join(", ", repetidos.Select(o => o.Codigo))}) que nombran el registro {repetidos.Key}: " +
                        "elija usted cuál es el bueno.";
                }
            }
            resultado.ConCertezaAlta = resultado.Propuestas.Count(p => p.CertezaAlta);

            // Al revés: registros que ponen un inmovilizado que no existe, está anulado o es de otra serie
            Dictionary<string, PropuestaInmovilizadoDTO> propuestaPorAparato = resultado.Propuestas
                .SelectMany(p => p.Candidatos.Where(c => c.Exacta).Select(c => (Clave: ReglaSituacionAparato.Clave(c.Producto) + "|" + ReglaSituacionAparato.Clave(c.NumeroSerie), p)))
                .GroupBy(x => x.Clave)
                .ToDictionary(g => g.Key, g => g.First().p, StringComparer.Ordinal);
            foreach (RegistroAparatoDTO r in registros.Where(r => !string.IsNullOrWhiteSpace(r.Inmovilizado)).OrderBy(r => r.Numero))
            {
                MotivoAparatoSinInmovilizado? motivo = null;
                string texto = null;
                string codigo = CodigoInmovilizado.Componer(r.Inmovilizado) ?? r.Inmovilizado.Trim();
                if (!fichas.TryGetValue(ClaveInmovilizado(r.Empresa, r.Inmovilizado), out InmovilizadoAparatoFila ficha))
                {
                    motivo = MotivoAparatoSinInmovilizado.FichaNoExiste;
                    texto = $"El registro {r.Numero} pone el inmovilizado {codigo}, pero no existe.";
                }
                else if (ficha.Estado < 0)
                {
                    motivo = MotivoAparatoSinInmovilizado.FichaAnulada;
                    texto = $"El registro {r.Numero} pone el inmovilizado {codigo}, que está anulado.";
                }
                else
                {
                    SerieLeidaInmovilizado leida = ReglaSerieInmovilizado.DeFicha(ficha);
                    if (leida != null && !string.IsNullOrWhiteSpace(r.NumeroSerie)
                        && !leida.Candidatas().Any(s => ReglaSituacionAparato.MismaSerie(s, r.NumeroSerie))
                        && !leida.Candidatas().Any(s => ReglaSeriesParecidas.Normalizar(s) == ReglaSeriesParecidas.Normalizar(r.NumeroSerie)))
                    {
                        motivo = MotivoAparatoSinInmovilizado.FichaConOtraSerie;
                        texto = $"El registro {r.Numero} (serie {r.NumeroSerie?.Trim()}) pone el inmovilizado {codigo}, pero su serie es {leida.Serie}.";
                    }
                }
                if (motivo == null)
                {
                    continue;
                }
                string claveAparato = ReglaSituacionAparato.Clave(r.Producto) + "|" + ReglaSituacionAparato.Clave(r.NumeroSerie);
                resultado.AparatosSinInmovilizado.Add(new AparatoSinInmovilizadoDTO
                {
                    Empresa = r.Empresa?.Trim(),
                    Registro = r.Numero,
                    Producto = r.Producto?.Trim(),
                    Nombre = nombres.TryGetValue(ReglaSituacionAparato.Clave(r.Producto), out string nombre) ? nombre : r.NombreProducto?.Trim(),
                    NumeroSerie = r.NumeroSerie?.Trim(),
                    InmovilizadoDelRegistro = r.Inmovilizado.Trim(),
                    Motivo = motivo.Value,
                    Texto = texto,
                    InmovilizadoPropuesto = propuestaPorAparato.TryGetValue(claveAparato, out PropuestaInmovilizadoDTO propuesta) ? propuesta.Codigo : null
                });
            }
            return resultado;
        }

        private static PropuestaInmovilizadoDTO Proponer(InmovilizadoAparatoFila ficha, SerieLeidaInmovilizado leida, ILookup<string, Aparato> porSerie,
            ILookup<string, Aparato> porForma, ILookup<string, RegistroAparatoDTO> registrosPorAparato, ILookup<string, MovimientoAparatoDTO> movimientosPorAparato,
            Dictionary<string, string> nombres)
        {
            string referencia = ReglaSituacionAparato.Clave(leida.Referencia);
            var propuesta = new PropuestaInmovilizadoDTO
            {
                Empresa = ficha.Empresa?.Trim(),
                Inmovilizado = ficha.Numero?.Trim(),
                Codigo = CodigoInmovilizado.Componer(ficha.Numero),
                Descripcion = ficha.Descripcion?.Trim(),
                FechaCreacion = ficha.FechaCreacion,
                SerieLeida = leida.Serie,
                ReferenciaLeida = leida.Referencia,
                OrigenSerie = ReglaSerieInmovilizado.Origen(ficha)
            };

            // 1. Exacta: la serie entera y, si no, el primer tramo; con referencia, solo de ese producto
            List<Aparato> exactas = new List<Aparato>();
            List<Aparato> deOtraReferencia = new List<Aparato>();
            foreach (string candidata in leida.Candidatas())
            {
                List<Aparato> conEsa = porSerie[ReglaSituacionAparato.Clave(candidata)].ToList();
                exactas = referencia.Length > 0 ? conEsa.Where(a => ReglaSituacionAparato.Clave(a.Producto) == referencia).ToList() : conEsa;
                if (exactas.Any())
                {
                    break;
                }
                if (deOtraReferencia.Count == 0)
                {
                    deOtraReferencia = conEsa;
                }
            }
            string notaParecido = null;
            List<Aparato> parecidas = new List<Aparato>();
            if (!exactas.Any())
            {
                if (deOtraReferencia.Any())
                {
                    parecidas = deOtraReferencia;
                    notaParecido = $"la serie es exacta pero la referencia escrita ({leida.Referencia}) es de otro producto";
                }
                else
                {
                    foreach (string candidata in leida.Candidatas())
                    {
                        List<Aparato> conForma = porForma[ReglaSeriesParecidas.Normalizar(candidata)].ToList();
                        List<Aparato> filtradas = referencia.Length > 0 ? conForma.Where(a => ReglaSituacionAparato.Clave(a.Producto) == referencia).ToList() : new List<Aparato>();
                        parecidas = filtradas.Any() ? filtradas : conForma;
                        if (parecidas.Any())
                        {
                            notaParecido = "la serie está escrita de otra forma (espacios, guiones, puntos, ceros a la izquierda u O/0 e I/1)";
                            break;
                        }
                    }
                    if (!parecidas.Any() && (ficha.Descripcion?.Trim().Length ?? 0) >= LONGITUD_DESCRIPCION && string.IsNullOrWhiteSpace(ficha.NumeroSerie))
                    {
                        // La descripción está llena: la serie puede venir cortada (sin quitar ceros: el corte puede caer en uno)
                        string forma = SinSeparadores(leida.Serie);
                        if (forma.Length >= 4)
                        {
                            parecidas = porForma.SelectMany(g => g)
                                .Where(a => SinSeparadores(a.Serie).StartsWith(forma, StringComparison.Ordinal) && SinSeparadores(a.Serie).Length > forma.Length)
                                .Where(a => referencia.Length == 0 || ReglaSituacionAparato.Clave(a.Producto) == referencia)
                                .ToList();
                            if (parecidas.Any())
                            {
                                notaParecido = "la descripción está cortada a 50 caracteres y la serie empieza igual";
                            }
                        }
                    }
                }
            }

            foreach (Aparato a in exactas.Concat(parecidas).OrderBy(a => a.Producto, StringComparer.Ordinal).ThenBy(a => a.Serie, StringComparer.Ordinal))
            {
                List<RegistroAparatoDTO> regs = registrosPorAparato[a.Clave].OrderBy(r => r.Numero).ToList();
                var candidato = new CandidatoEnlaceInmovilizadoDTO
                {
                    Producto = a.Producto,
                    Nombre = nombres.TryGetValue(ReglaSituacionAparato.Clave(a.Producto), out string nombre) ? nombre : regs.FirstOrDefault()?.NombreProducto?.Trim(),
                    NumeroSerie = a.Serie,
                    Exacta = exactas.Contains(a),
                    Registros = regs.Select(r => r.Numero).ToList(),
                    SituacionCalculada = ReglaSituacionAparato.Calcular(movimientosPorAparato[a.Clave], regs.LastOrDefault())
                };
                candidato.Motivo = MotivoNoEnlazable(regs, propuesta.Empresa, propuesta.Codigo);
                candidato.Aplicable = candidato.Motivo == null;
                propuesta.Candidatos.Add(candidato);
            }

            if (exactas.Count == 1)
            {
                propuesta.Certeza = CertezaEnlaceInmovilizado.UnaCoincidenciaExacta;
                CandidatoEnlaceInmovilizadoDTO unico = propuesta.Candidatos.First(c => c.Exacta);
                propuesta.TextoCerteza = $"La serie {unico.NumeroSerie} solo está en el aparato {unico.Producto}" +
                    (unico.Registros.Any() ? $" (registro {string.Join(", ", unico.Registros)})." : ", que no tiene registro en Alquileres.");
                if (unico.Aplicable)
                {
                    propuesta.CertezaAlta = true;
                    propuesta.RegistroPropuesto = unico.Registros.Single();
                    propuesta.ProductoPropuesto = unico.Producto;
                    propuesta.SeriePropuesta = unico.NumeroSerie;
                }
                else
                {
                    propuesta.MotivoNoAplicable = unico.Motivo;
                }
            }
            else if (exactas.Count > 1)
            {
                propuesta.Certeza = CertezaEnlaceInmovilizado.VariasCoincidencias;
                propuesta.TextoCerteza = $"La serie {leida.Serie} está en {exactas.Count} aparatos ({string.Join(", ", exactas.Select(a => a.Producto).OrderBy(x => x, StringComparer.Ordinal))}).";
                propuesta.MotivoNoAplicable = "Hay más de un aparato con esa serie: indique usted el registro.";
            }
            else if (parecidas.Any())
            {
                propuesta.Certeza = CertezaEnlaceInmovilizado.SoloPorParecido;
                propuesta.TextoCerteza = $"Ningún aparato tiene exactamente la serie {leida.Serie}; se parece a {string.Join(", ", parecidas.Select(a => $"{a.Producto} {a.Serie}"))}: {notaParecido}.";
                propuesta.MotivoNoAplicable = "Solo se parece: compruebe usted el aparato e indique el registro.";
            }
            else
            {
                propuesta.Certeza = CertezaEnlaceInmovilizado.Ninguna;
                propuesta.TextoCerteza = $"Ningún aparato tiene la serie {leida.Serie}" +
                    (leida.Referencia == null ? string.Empty : $" en el producto {leida.Referencia}") + ": ni compra, ni venta, ni registro de Alquileres.";
                propuesta.MotivoNoAplicable = "No hay ningún aparato con esa serie.";
            }
            return propuesta;
        }

        /// <summary>Mayúsculas y sin espacios, guiones ni puntos (para la serie cortada).</summary>
        internal static string SinSeparadores(string serie)
            => new string((serie ?? string.Empty).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.').ToArray());

        /// <summary>Por qué no se puede enlazar con esos registros (de usted); null si se puede (uno solo, misma empresa, sin inmovilizado).</summary>
        internal static string MotivoNoEnlazable(List<RegistroAparatoDTO> registros, string empresa, string codigo)
        {
            if (registros == null || registros.Count == 0)
            {
                return "El aparato no tiene registro en Alquileres: dé de alta el registro y vuelva a mirar la propuesta.";
            }
            if (registros.Count > 1)
            {
                return $"La serie está en {registros.Count} registros ({string.Join(", ", registros.Select(x => x.Numero))}): indique usted cuál.";
            }
            RegistroAparatoDTO r = registros[0];
            if (ReglaSituacionAparato.Clave(r.Empresa) != ReglaSituacionAparato.Clave(empresa))
            {
                return $"El registro {r.Numero} es de la empresa {r.Empresa?.Trim()} y el inmovilizado {codigo}, de la {empresa}.";
            }
            if (!string.IsNullOrWhiteSpace(r.Inmovilizado))
            {
                return $"El registro {r.Numero} ya tiene el inmovilizado {CodigoInmovilizado.Componer(r.Inmovilizado) ?? r.Inmovilizado.Trim()}.";
            }
            return null;
        }
    }

    // ---------------------------------------------------------------- el enlace

    /// <summary>POST api/Aparatos/Inmovilizados/Enlazar: qué enlazar.</summary>
    public class PeticionEnlaceInmovilizadosDTO
    {
        /// <summary>En bloque: todas las propuestas de certeza alta.</summary>
        public bool TodasLasDeCertezaAlta { get; set; }
        /// <summary>Uno a uno: el inmovilizado y, si se indica, el registro (el par a mano, aunque no sea de certeza alta).</summary>
        public List<ParEnlaceInmovilizadoDTO> Pares { get; set; } = new List<ParEnlaceInmovilizadoDTO>();
    }

    public class ParEnlaceInmovilizadoDTO
    {
        /// <summary>Sin ella, la 1.</summary>
        public string Empresa { get; set; }
        /// <summary>«1824» o «I/1824».</summary>
        public string Inmovilizado { get; set; }
        /// <summary>El número del registro de Alquileres. Sin él, el de la propuesta (solo si es de certeza alta).</summary>
        public int? Registro { get; set; }
    }

    public class FilaEnlaceInmovilizadoDTO
    {
        public string Empresa { get; set; }
        public string Inmovilizado { get; set; }
        public string Codigo { get; set; }
        public int? Registro { get; set; }
        public string Producto { get; set; }
        public string NumeroSerie { get; set; }
        /// <summary>«CertezaAlta» (de la propuesta) o «AMano» (el par indicado).</summary>
        public string Origen { get; set; }
        /// <summary>Se enlazaría (ensayo) o se ha enlazado.</summary>
        public bool Ok { get; set; }
        /// <summary>Por qué no (de usted); null si Ok.</summary>
        public string Motivo { get; set; }

        [JsonIgnore] internal string NumeroTalCual { get; set; }
    }

    public class ResultadoEnlaceInmovilizadosDTO
    {
        public bool Ensayo { get; set; }
        /// <summary>Filas Ok (en ensayo, las que se enlazarían).</summary>
        public int Enlazados { get; set; }
        /// <summary>Con el script Issue913b, el producto y la serie se escriben también en la ficha del inmovilizado.</summary>
        public bool EscribeColumnasEnInmovilizados { get; set; }
        public List<FilaEnlaceInmovilizadoDTO> Filas { get; set; } = new List<FilaEnlaceInmovilizadoDTO>();
    }

    /// <summary>
    /// NestoAPI#913 corte 2: LA REGLA (pura) de qué se enlaza. En bloque, las propuestas de certeza alta. Uno a uno, el par: sin
    /// registro, el de la propuesta si es de certeza alta; con registro, ese (a mano), si el inmovilizado existe, está vivo y no está
    /// ya en otro registro, y el registro existe, es de la misma empresa y no tiene otro inmovilizado. Un registro no se enlaza dos
    /// veces en la misma petición.
    /// </summary>
    public static class ReglaEnlaceInmovilizados
    {
        public const int MAXIMO_PARES = 500;

        public static List<FilaEnlaceInmovilizadoDTO> Planificar(PeticionEnlaceInmovilizadosDTO peticion, DatosAparatos datos, PropuestasInmovilizadosDTO propuestas)
        {
            var filas = new List<FilaEnlaceInmovilizadoDTO>();
            datos = datos ?? new DatosAparatos();
            propuestas = propuestas ?? new PropuestasInmovilizadosDTO();
            List<RegistroAparatoDTO> registros = datos.Registros ?? new List<RegistroAparatoDTO>();
            Dictionary<string, InmovilizadoAparatoFila> fichas = (datos.Inmovilizados ?? new List<InmovilizadoAparatoFila>())
                .GroupBy(i => ReglaPropuestasInmovilizados.ClaveInmovilizado(i.Empresa, i.Numero))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Dictionary<string, PropuestaInmovilizadoDTO> porInmovilizado = propuestas.Propuestas
                .GroupBy(p => ReglaPropuestasInmovilizados.ClaveInmovilizado(p.Empresa, p.Inmovilizado))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            if (peticion?.TodasLasDeCertezaAlta == true)
            {
                foreach (PropuestaInmovilizadoDTO p in propuestas.Propuestas.Where(p => p.CertezaAlta))
                {
                    filas.Add(new FilaEnlaceInmovilizadoDTO
                    {
                        Empresa = p.Empresa,
                        Inmovilizado = CodigoInmovilizado.Normalizar(p.Inmovilizado),
                        Codigo = p.Codigo,
                        Registro = p.RegistroPropuesto,
                        Producto = p.ProductoPropuesto,
                        NumeroSerie = p.SeriePropuesta,
                        Origen = "CertezaAlta",
                        Ok = true,
                        NumeroTalCual = p.Inmovilizado
                    });
                }
            }

            foreach (ParEnlaceInmovilizadoDTO par in peticion?.Pares ?? new List<ParEnlaceInmovilizadoDTO>())
            {
                if (par == null)
                {
                    continue;
                }
                string empresa = string.IsNullOrWhiteSpace(par.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : par.Empresa.Trim();
                string pedido = par.Inmovilizado?.Trim() ?? string.Empty;
                string numero = CodigoInmovilizado.Leer(pedido) ?? (pedido.Length > 0 && pedido.All(char.IsDigit) ? CodigoInmovilizado.Normalizar(pedido) : null);
                var fila = new FilaEnlaceInmovilizadoDTO { Empresa = empresa, Inmovilizado = numero ?? pedido, Codigo = CodigoInmovilizado.Componer(numero), Registro = par.Registro, Origen = "AMano" };
                filas.Add(fila);
                if (numero == null)
                {
                    fila.Motivo = $"«{pedido}» no es un número de inmovilizado.";
                    continue;
                }
                string clave = ReglaPropuestasInmovilizados.ClaveInmovilizado(empresa, numero);
                if (!fichas.TryGetValue(clave, out InmovilizadoAparatoFila ficha))
                {
                    fila.Motivo = $"No existe el inmovilizado {fila.Codigo} en la empresa {empresa}.";
                    continue;
                }
                fila.NumeroTalCual = ficha.Numero?.Trim();
                if (ficha.Estado < 0)
                {
                    fila.Motivo = $"El inmovilizado {fila.Codigo} está anulado.";
                    continue;
                }
                RegistroAparatoDTO yaEn = registros.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Inmovilizado)
                    && ReglaPropuestasInmovilizados.ClaveInmovilizado(r.Empresa, r.Inmovilizado) == clave);
                if (yaEn != null)
                {
                    fila.Registro = fila.Registro ?? yaEn.Numero;
                    fila.Motivo = yaEn.Numero == par.Registro
                        ? $"El registro {yaEn.Numero} ya tiene el inmovilizado {fila.Codigo}: no hay nada que hacer."
                        : $"El inmovilizado {fila.Codigo} ya está en el registro {yaEn.Numero}: quítelo de allí antes en la ventana de Alquileres.";
                    continue;
                }
                if (!par.Registro.HasValue)
                {
                    fila.Origen = "CertezaAlta";
                    if (!porInmovilizado.TryGetValue(clave, out PropuestaInmovilizadoDTO propuesta) || !propuesta.CertezaAlta)
                    {
                        fila.Motivo = propuesta?.MotivoNoAplicable ?? $"El inmovilizado {fila.Codigo} no tiene una propuesta de certeza alta: indique usted el registro.";
                        continue;
                    }
                    fila.Registro = propuesta.RegistroPropuesto;
                    fila.Producto = propuesta.ProductoPropuesto;
                    fila.NumeroSerie = propuesta.SeriePropuesta;
                    fila.Ok = true;
                    continue;
                }
                RegistroAparatoDTO registro = registros.FirstOrDefault(r => r.Numero == par.Registro.Value
                    && ReglaSituacionAparato.Clave(r.Empresa) == ReglaSituacionAparato.Clave(empresa));
                if (registro == null)
                {
                    fila.Motivo = $"No existe el registro {par.Registro.Value} de Alquileres en la empresa {empresa}.";
                    continue;
                }
                fila.Producto = registro.Producto?.Trim();
                fila.NumeroSerie = registro.NumeroSerie?.Trim();
                string motivo = ReglaPropuestasInmovilizados.MotivoNoEnlazable(new List<RegistroAparatoDTO> { registro }, empresa, fila.Codigo);
                if (motivo != null)
                {
                    fila.Motivo = motivo;
                    continue;
                }
                fila.Ok = true;
            }

            // Ni un inmovilizado ni un registro dos veces: la primera manda
            var registrosUsados = new HashSet<int>();
            var inmovilizadosUsados = new HashSet<string>(StringComparer.Ordinal);
            foreach (FilaEnlaceInmovilizadoDTO fila in filas.Where(f => f.Ok))
            {
                string claveInmovilizado = ReglaPropuestasInmovilizados.ClaveInmovilizado(fila.Empresa, fila.Inmovilizado);
                if (!inmovilizadosUsados.Add(claveInmovilizado))
                {
                    fila.Ok = false;
                    fila.Motivo = $"El inmovilizado {fila.Codigo} ya va en otra fila de esta petición.";
                    continue;
                }
                if (!registrosUsados.Add(fila.Registro.Value))
                {
                    fila.Ok = false;
                    fila.Motivo = $"El registro {fila.Registro} ya se enlaza en otra fila de esta petición.";
                }
            }
            return filas;
        }
    }

    /// <summary>Un enlace a escribir: el registro de Alquileres, el número del inmovilizado tal cual está en la tabla, y el aparato.</summary>
    public class EnlaceInmovilizado
    {
        public string Empresa { get; set; }
        public int Registro { get; set; }
        public string Inmovilizado { get; set; }
        public string Producto { get; set; }
        public string NumeroSerie { get; set; }
    }

    public interface IEscritorEnlacesInmovilizado
    {
        /// <summary>
        /// Escribe el enlace (y su rastro en Modificaciones) si el registro sigue igual que se leyó (mismo producto y serie, sin
        /// inmovilizado) y el inmovilizado sigue vivo y sin otro registro. False si algo ha cambiado entretanto.
        /// </summary>
        Task<bool> Enlazar(EnlaceInmovilizado enlace, string usuario);
    }

    public interface IServicioEnlaceInmovilizados
    {
        Task<PropuestasInmovilizadosDTO> Propuestas();
        /// <exception cref="ArgumentException">Sin nada que enlazar o demasiados pares: el motivo es para el usuario.</exception>
        Task<ResultadoEnlaceInmovilizadosDTO> Enlazar(PeticionEnlaceInmovilizadosDTO peticion, bool ensayo, string usuario);
    }

    /// <summary>Lee (todo el alcance de aparatos), propone (<see cref="ReglaPropuestasInmovilizados"/>) y, sin ensayo, escribe cada enlace por separado.</summary>
    public class ServicioEnlaceInmovilizados : IServicioEnlaceInmovilizados
    {
        private readonly ILectorAparatos lector;
        private readonly IEscritorEnlacesInmovilizado escritor;

        public ServicioEnlaceInmovilizados(ILectorAparatos lector, IEscritorEnlacesInmovilizado escritor)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.escritor = escritor ?? throw new ArgumentNullException(nameof(escritor));
        }

        public async Task<PropuestasInmovilizadosDTO> Propuestas()
        {
            DatosAparatos datos = await lector.Leer(new FiltroAparatos()).ConfigureAwait(false);
            return ReglaPropuestasInmovilizados.Calcular(datos);
        }

        public async Task<ResultadoEnlaceInmovilizadosDTO> Enlazar(PeticionEnlaceInmovilizadosDTO peticion, bool ensayo, string usuario)
        {
            int pares = peticion?.Pares?.Count(p => p != null) ?? 0;
            if (peticion == null || (!peticion.TodasLasDeCertezaAlta && pares == 0))
            {
                throw new ArgumentException("Diga qué inmovilizados enlazar (los pares) o pida todas las propuestas de certeza alta.");
            }
            if (pares > ReglaEnlaceInmovilizados.MAXIMO_PARES)
            {
                throw new ArgumentException($"Como mucho se enlazan {ReglaEnlaceInmovilizados.MAXIMO_PARES} pares de una vez.");
            }
            DatosAparatos datos = await lector.Leer(new FiltroAparatos()).ConfigureAwait(false);
            PropuestasInmovilizadosDTO propuestas = ReglaPropuestasInmovilizados.Calcular(datos);
            var resultado = new ResultadoEnlaceInmovilizadosDTO
            {
                Ensayo = ensayo,
                EscribeColumnasEnInmovilizados = datos?.ColumnasAparatoEnInmovilizados == true,
                Filas = ReglaEnlaceInmovilizados.Planificar(peticion, datos, propuestas)
            };
            if (!ensayo)
            {
                foreach (FilaEnlaceInmovilizadoDTO fila in resultado.Filas.Where(f => f.Ok))
                {
                    bool escrito = await escritor.Enlazar(new EnlaceInmovilizado
                    {
                        Empresa = fila.Empresa,
                        Registro = fila.Registro.Value,
                        Inmovilizado = fila.NumeroTalCual ?? fila.Inmovilizado,
                        Producto = fila.Producto,
                        NumeroSerie = fila.NumeroSerie
                    }, usuario).ConfigureAwait(false);
                    if (!escrito)
                    {
                        fila.Ok = false;
                        fila.Motivo = $"El registro {fila.Registro} o el inmovilizado {fila.Codigo} han cambiado mientras tanto: vuelva a mirar la propuesta.";
                    }
                }
            }
            resultado.Enlazados = resultado.Filas.Count(f => f.Ok);
            return resultado;
        }
    }
}
