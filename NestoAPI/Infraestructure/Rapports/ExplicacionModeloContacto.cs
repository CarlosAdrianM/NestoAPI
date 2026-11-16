using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Calibrators;
using Microsoft.ML.Trainers;
using ModeloContactoEntrada = ModeloLlamadaPedido.Features.ModeloContactoEntrada;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#866 (corte 2): por qué el modelo de contactos da esa probabilidad a un cliente. ML.NET 4.0.2 calcula la contribución de
    /// cada posición del vector Features a la puntuación de un árbol (CalculateFeatureContribution, sin reentrenar nada); aquí se suman
    /// por rasgo (las posiciones del one-hot de una categórica son un solo rasgo) y las <see cref="CAUSAS"/> que más empujan hacia el
    /// pedido se escriben con una plantilla por rasgo (<see cref="Frase"/>), con los valores del propio cliente para que el vendedor
    /// pueda comprobarlas.
    /// </summary>
    public static class ExplicacionModeloContacto
    {
        /// <summary>Causas que se dicen (las que más suben la probabilidad).</summary>
        public const int CAUSAS = 2;
        public const string PREFIJO = "Sobre todo porque ";
        /// <summary>Contribución mínima (en la escala de la puntuación del árbol) para contarla como causa.</summary>
        public const float CONTRIBUCION_MINIMA = 0.0001f;
        private const string COLUMNA = "FeatureContributions";

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>
        /// La contribución de cada rasgo (nombre de la propiedad de <see cref="ModeloContactoEntrada"/>) para cada entrada, en el
        /// mismo orden. Lanza si el modelo no permite calcularla (lo recoge quien llama).
        /// </summary>
        public static List<Dictionary<string, float>> Contribuciones(ITransformer modelo, MLContext ml, IList<ModeloContactoEntrada> entradas)
        {
            var resultado = new List<Dictionary<string, float>>();
            if (entradas == null || entradas.Count == 0)
            {
                return resultado;
            }
            ITransformer prediccion = UltimoTransformador(modelo)
                ?? throw new InvalidOperationException("el modelo no termina en un transformador de predicción");
            IEstimator<ITransformer> estimador = EstimadorContribuciones(ml, prediccion);
            IDataView transformados = modelo.Transform(ml.Data.LoadFromEnumerable(entradas));
            IDataView conContribuciones = estimador.Fit(transformados).Transform(transformados);

            VBuffer<ReadOnlyMemory<char>> nombres = default;
            transformados.Schema["Features"].GetSlotNames(ref nombres);
            string[] rasgos = nombres.DenseValues().Select(n => Rasgo(n.ToString())).ToArray();

            foreach (VBuffer<float> fila in conContribuciones.GetColumn<VBuffer<float>>(conContribuciones.Schema[COLUMNA]))
            {
                var porRasgo = new Dictionary<string, float>(StringComparer.Ordinal);
                float[] valores = fila.DenseValues().ToArray();
                for (int i = 0; i < valores.Length && i < rasgos.Length; i++)
                {
                    porRasgo[rasgos[i]] = (porRasgo.TryGetValue(rasgos[i], out float suma) ? suma : 0f) + valores[i];
                }
                resultado.Add(porRasgo);
            }
            return resultado;
        }

        /// <summary>El rasgo de una posición de Features: «MesCod.11» → «Mes»; «Pedidos12Meses» → «Pedidos12Meses».</summary>
        public static string Rasgo(string slot)
        {
            string nombre = slot ?? string.Empty;
            int punto = nombre.IndexOf('.');
            if (punto >= 0)
            {
                nombre = nombre.Substring(0, punto);
            }
            return nombre.EndsWith("Cod", StringComparison.Ordinal) ? nombre.Substring(0, nombre.Length - 3) : nombre;
        }

        private static ITransformer UltimoTransformador(ITransformer modelo)
        {
            ITransformer actual = modelo;
            while (actual is TransformerChain<ITransformer> cadena)
            {
                actual = cadena.LastTransformer;
            }
            return actual;
        }

        /// <summary>
        /// El estimador de contribuciones para el transformador de predicción: el árbol sin calibrar directamente o, el nuestro (binario
        /// calibrado con Platt), por la sobrecarga genérica de los modelos calibrados con los tipos que tenga al cargarlo del zip.
        /// </summary>
        private static IEstimator<ITransformer> EstimadorContribuciones(MLContext ml, ITransformer prediccion)
        {
            if (prediccion is ISingleFeaturePredictionTransformer<ICalculateFeatureContribution> directo)
            {
                return ml.Transforms.CalculateFeatureContribution(directo, numberOfPositiveContributions: 1000, numberOfNegativeContributions: 1000, normalize: false);
            }
            object modeloParametros = prediccion.GetType().GetProperty("Model")?.GetValue(prediccion)
                ?? throw new InvalidOperationException($"no se encuentra el modelo de {prediccion.GetType().Name}");
            Type calibrado = TipoBase(modeloParametros.GetType(), typeof(CalibratedModelParametersBase<,>))
                ?? throw new InvalidOperationException($"el modelo {modeloParametros.GetType().Name} no calcula contribuciones");
            MethodInfo generico = typeof(ExplainabilityCatalog).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(ExplainabilityCatalog.CalculateFeatureContribution) && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().Length == 2);
            MethodInfo cerrado = generico.MakeGenericMethod(calibrado.GetGenericArguments());
            return (IEstimator<ITransformer>)cerrado.Invoke(null, new object[] { ml.Transforms, prediccion, 1000, 1000, false });
        }

        private static Type TipoBase(Type tipo, Type genericoAbierto)
        {
            for (Type t = tipo; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == genericoAbierto)
                {
                    return t;
                }
            }
            return null;
        }

        // ---------------- Textos (puros) ----------------

        /// <summary>
        /// «Sobre todo porque lleva 64 días sin pedir y suele pedir cuando se le llama (el 60 % de las veces)»: las <see cref="CAUSAS"/>
        /// de más contribución positiva que tengan frase. Null si ninguna empuja hacia el pedido.
        /// </summary>
        public static string Explicacion(IDictionary<string, float> contribuciones, ModeloContactoEntrada entrada, int causas = CAUSAS)
        {
            if (contribuciones == null || entrada == null)
            {
                return null;
            }
            List<string> frases = contribuciones
                .Where(c => c.Value > CONTRIBUCION_MINIMA)
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => Frase(c.Key, entrada))
                .Where(f => !string.IsNullOrEmpty(f))
                .Take(causas)
                .ToList();
            if (!frases.Any())
            {
                return null;
            }
            return PREFIJO + (frases.Count == 1 ? frases[0] : string.Join(", ", frases.Take(frases.Count - 1)) + " y " + frases.Last());
        }

        /// <summary>La frase de un rasgo con el valor del cliente, de usted y comprobable; null si el rasgo no se sabe contar.</summary>
        public static string Frase(string rasgo, ModeloContactoEntrada e)
        {
            switch (rasgo)
            {
                case nameof(ModeloContactoEntrada.DiasDesdeUltimoPedido):
                    return e.DiasDesdeUltimoPedido >= 1000 ? "no ha pedido en dos años" : $"lleva {Entero(e.DiasDesdeUltimoPedido)} días sin pedir";
                case nameof(ModeloContactoEntrada.DiasEntrePedidos):
                    return e.DiasEntrePedidos >= 1000 ? null : $"suele pedir cada {Entero(e.DiasEntrePedidos)} días";
                case nameof(ModeloContactoEntrada.Pedidos12Meses):
                    return e.Pedidos12Meses == 1 ? "ha hecho 1 pedido en el último año" : $"ha hecho {Entero(e.Pedidos12Meses)} pedidos en el último año";
                case nameof(ModeloContactoEntrada.Importe12Meses):
                    return $"ha comprado {Euros(e.Importe12Meses)} en el último año";
                case nameof(ModeloContactoEntrada.ImporteMedioPedido):
                    return e.ImporteMedioPedido <= 0 ? null : $"su pedido medio es de {Euros(e.ImporteMedioPedido)}";
                case nameof(ModeloContactoEntrada.TendenciaImporte):
                    return e.TendenciaImporte > 0.05f ? "está comprando más que hace seis meses"
                        : e.TendenciaImporte < -0.05f ? "está comprando menos que hace seis meses" : null;
                case nameof(ModeloContactoEntrada.TasaConversionCliente):
                    return e.ContactosPrevios > 0
                        ? $"suele pedir cuando se le contacta (el {Entero(e.TasaConversionCliente * 100)} % de las veces)"
                        : null;
                case nameof(ModeloContactoEntrada.ContactosPrevios):
                    return e.ContactosPrevios == 1 ? "se le ha contactado 1 vez en dos años" : $"se le ha contactado {Entero(e.ContactosPrevios)} veces en dos años";
                case nameof(ModeloContactoEntrada.PedidosMismoMesAnnoAnterior):
                    return e.PedidosMismoMesAnnoAnterior <= 0 ? "el año pasado por estas fechas no pidió"
                        : e.PedidosMismoMesAnnoAnterior == 1 ? "el año pasado por estas fechas pidió" : $"el año pasado por estas fechas hizo {Entero(e.PedidosMismoMesAnnoAnterior)} pedidos";
                case nameof(ModeloContactoEntrada.Mes):
                    return int.TryParse(e.Mes, out int mes) && mes >= 1 && mes <= 12 ? $"{es.DateTimeFormat.GetMonthName(mes)} suele ser buen mes para él" : null;
                case nameof(ModeloContactoEntrada.DiaSemana):
                    return int.TryParse(e.DiaSemana, out int dia) && dia >= 1 && dia <= 7 ? $"el {es.DateTimeFormat.GetDayName((DayOfWeek)(dia % 7))} es buen día para él" : null;
                case nameof(ModeloContactoEntrada.TipoInteraccion):
                    return string.IsNullOrWhiteSpace(e.TipoInteraccion) ? null : $"con él funciona bien el contacto por {TipoEnTexto(e.TipoInteraccion)}";
                case nameof(ModeloContactoEntrada.GrupoSubgrupoMasVendido):
                    return string.IsNullOrWhiteSpace(e.GrupoSubgrupoMasVendido) || e.GrupoSubgrupoMasVendido == CalculadoraFeaturesContacto.SIN_GRUPO
                        ? null : $"lo que más compra ({e.GrupoSubgrupoMasVendido.Trim()}) se pide mucho en esta época";
                case nameof(ModeloContactoEntrada.EsPorLaTarde):
                    return e.EsPorLaTarde > 0 ? "por la tarde es buen momento para él" : "por la mañana es buen momento para él";
                default:
                    return null;
            }
        }

        /// <summary>
        /// El motivo de siempre con la explicación detrás («Compra cada 12 días…. Sobre todo porque …»). Sin pasar de
        /// <see cref="MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO"/>: si no cabe entera, la primera causa sola; si tampoco, el motivo como estaba.
        /// </summary>
        /// <param name="reservar">Caracteres que hay que dejar libres detrás (el « (hablado hoy)» que se añade después).</param>
        public static string ConExplicacion(string motivo, string explicacion, int reservar = 0)
        {
            if (string.IsNullOrWhiteSpace(explicacion))
            {
                return motivo;
            }
            string baseMotivo = (motivo ?? string.Empty).TrimEnd().TrimEnd('.');
            if (baseMotivo.EndsWith(explicacion.TrimEnd('.'), StringComparison.Ordinal) || (motivo ?? string.Empty).Contains(PREFIJO))
            {
                return motivo;
            }
            string Unir(string texto) => string.IsNullOrEmpty(baseMotivo) ? texto + "." : $"{baseMotivo}. {texto}.";
            string completo = Unir(explicacion.TrimEnd('.'));
            int maximo = MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO - Math.Max(0, reservar);
            if (completo.Length <= maximo)
            {
                return completo;
            }
            int y = explicacion.IndexOf(" y ", PREFIJO.Length, StringComparison.Ordinal);
            int coma = explicacion.IndexOf(", ", PREFIJO.Length, StringComparison.Ordinal);
            int corte = new[] { y, coma }.Where(i => i > 0).DefaultIfEmpty(-1).Min();
            if (corte > 0)
            {
                string primera = Unir(explicacion.Substring(0, corte));
                if (primera.Length <= maximo)
                {
                    return primera;
                }
            }
            return motivo;
        }

        private static string TipoEnTexto(string tipo)
        {
            switch (tipo.Trim())
            {
                case "Llamada": return "teléfono";
                case "Visita": return "visita";
                case "WhatsApp": return "WhatsApp";
                default: return tipo.Trim().ToLower(es);
            }
        }

        private static string Entero(float valor) => Math.Round(valor, MidpointRounding.AwayFromZero).ToString("N0", es);
        private static string Euros(float valor) => Math.Round(valor, MidpointRounding.AwayFromZero).ToString("N0", es) + " €";
    }
}
