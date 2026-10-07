using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#603 c3b: carga y puntuación del modelo de contactos (ModelsIA/modelo_llamadas.zip, entrada
    /// <see cref="ModeloContactoEntrada"/>). Lo usan las sugerencias de contacto y el endpoint antiguo
    /// GetClientesProbabilidadVenta: un solo zip para los dos.
    /// <para>El modelo se entrena con LightGBM (repo ModeloLlamadaPedido). Para puntuar basta el ensamblado gestionado
    /// Microsoft.ML.LightGbm (el árbol se evalúa en .NET; la librería nativa lib_lightgbm solo hace falta para entrenar), pero
    /// hay que registrarlo en el catálogo: si no, ML.NET no reconoce el cargador del zip.</para>
    /// </summary>
    public static class ModeloContacto
    {
        private static readonly object bloqueo = new object();
        private static string rutaCargada;
        private static DateTime fechaCargada;
        private static ITransformer modeloCargado;
        private static MLContext contextoCargado;

        // TODO NestoAPI#603 c3b: copiar modelo_llamadas_AAAAMMDD.zip (ejecución completa de ModeloLlamadaPedido) a
        // ModelsIA/modelo_llamadas.zip EN EL MISMO DEPLOY que este código. El zip que hay ahora es el de nov-2024 (entrada
        // ClienteInteraccion) y no es compatible: hasta cambiarlo, el modelo falla, se registra en ELMAH, las sugerencias
        // salen sin probabilidad y GetClientesProbabilidadVenta da error.
        public static string RutaPorDefecto => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModelsIA", "modelo_llamadas.zip");

        internal static MLContext CrearContexto()
        {
            var ml = new MLContext(seed: 603);
            ml.ComponentCatalog.RegisterAssembly(typeof(LightGbmBinaryModelParameters).Assembly);
            return ml;
        }

        /// <summary>
        /// El modelo del zip con su contexto; se guarda en memoria hasta que cambie el fichero (cargarlo cuesta más que puntuar).
        /// </summary>
        internal static ITransformer Cargar(string ruta, out MLContext ml)
        {
            if (!File.Exists(ruta))
            {
                throw new FileNotFoundException("NestoAPI#603: no está el modelo de contactos", ruta);
            }
            DateTime fecha = File.GetLastWriteTimeUtc(ruta);
            lock (bloqueo)
            {
                if (modeloCargado == null || rutaCargada != ruta || fechaCargada != fecha)
                {
                    MLContext nuevo = CrearContexto();
                    using (var fichero = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        modeloCargado = nuevo.Model.Load(fichero, out _);
                    }
                    contextoCargado = nuevo;
                    rutaCargada = ruta;
                    fechaCargada = fecha;
                }
                ml = contextoCargado;
                return modeloCargado;
            }
        }

        /// <summary>Probabilidad de pedido de cada entrada, en el mismo orden.</summary>
        public static List<float> Puntuar(IList<ModeloContactoEntrada> entradas, string ruta = null)
        {
            if (entradas == null || entradas.Count == 0)
            {
                return new List<float>();
            }
            ITransformer modelo = Cargar(ruta ?? RutaPorDefecto, out MLContext ml);
            IDataView datos = ml.Data.LoadFromEnumerable(entradas);
            return ml.Data.CreateEnumerable<PrediccionModelo>(modelo.Transform(datos), reuseRowObject: false)
                .Select(p => p.Probability)
                .ToList();
        }

        /// <summary>
        /// Copia de las entradas con el grupo/subgrupo que se pregunta (como el endpoint antiguo, que lo pisaba en todas).
        /// Vacío = el que más compra cada cliente. Nunca se tocan las entradas originales.
        /// </summary>
        public static List<ModeloContactoEntrada> ConGrupoSubgrupo(IEnumerable<ModeloContactoEntrada> entradas, string grupoSubgrupo)
        {
            bool pisar = !string.IsNullOrWhiteSpace(grupoSubgrupo);
            return entradas.Select(e => new ModeloContactoEntrada
            {
                ClienteId = e.ClienteId,
                TipoInteraccion = e.TipoInteraccion,
                Mes = e.Mes,
                DiaSemana = e.DiaSemana,
                GrupoSubgrupoMasVendido = pisar ? grupoSubgrupo.Trim() : e.GrupoSubgrupoMasVendido,
                EsPorLaTarde = e.EsPorLaTarde,
                Pedidos12Meses = e.Pedidos12Meses,
                Importe12Meses = e.Importe12Meses,
                DiasDesdeUltimoPedido = e.DiasDesdeUltimoPedido,
                DiasEntrePedidos = e.DiasEntrePedidos,
                PedidosMismoMesAnnoAnterior = e.PedidosMismoMesAnnoAnterior,
                ImporteMedioPedido = e.ImporteMedioPedido,
                TendenciaImporte = e.TendenciaImporte,
                TasaConversionCliente = e.TasaConversionCliente,
                ContactosPrevios = e.ContactosPrevios,
                SinHistorial = e.SinHistorial
            }).ToList();
        }

        /// <summary>Las entradas de hoy (con <paramref name="ahora"/> como momento del contacto) para cada historial.</summary>
        public static List<ModeloContactoEntrada> Entradas(IEnumerable<HistorialContacto> historiales, DateTime ahora, string tipoInteraccion)
        {
            string tipo = GestorClientes.NormalizarTipoInteraccion(tipoInteraccion);
            return historiales.Select(h => CalculadoraFeaturesContacto.Calcular(h, ahora, tipo)).ToList();
        }
    }
}
