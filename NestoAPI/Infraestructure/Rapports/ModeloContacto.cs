using Microsoft.ML;
using ModeloLlamadaPedido.Entrenamiento;
using NestoAPI.Models.Clientes;
using ModeloContactoEntrada = ModeloLlamadaPedido.Features.ModeloContactoEntrada;
using ModeloContactoSubgrupoEntrada = ModeloLlamadaPedido.Features.ModeloContactoSubgrupoEntrada;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#603 c3b: carga y puntuación del modelo de contactos (<see cref="RutaPorDefecto"/>, entrada
    /// <see cref="ModeloContactoEntrada"/>). Lo usan las sugerencias de contacto y el endpoint antiguo
    /// GetClientesProbabilidadVenta: un solo zip para los dos.
    /// <para>El modelo se entrena con LightGBM (núcleo ModeloLlamadaPedido.Nucleo; cada mes, el job de NestoAPI#619). Para puntuar basta el ensamblado gestionado
    /// Microsoft.ML.LightGbm (el árbol se evalúa en .NET; la librería nativa lib_lightgbm solo hace falta para entrenar), pero
    /// hay que registrarlo en el catálogo: si no, ML.NET no reconoce el cargador del zip.</para>
    /// </summary>
    public static class ModeloContacto
    {
        private static readonly object bloqueo = new object();
        /// <summary>NestoAPI#618: un modelo cargado por ruta (el general y el de subgrupo a la vez, sin recargar al alternar).</summary>
        private static readonly Dictionary<string, ModeloCargado> cargados = new Dictionary<string, ModeloCargado>(StringComparer.OrdinalIgnoreCase);

        private sealed class ModeloCargado
        {
            public DateTime Fecha;
            public ITransformer Modelo;
            public MLContext Contexto;
        }

        /// <summary>El zip que va en el deploy (ModelsIA del proyecto).</summary>
        public static string RutaDelDeploy => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModelsIA", "modelo_llamadas.zip");

        /// <summary>
        /// NestoAPI#619: el que reentrena el job mensual (fuera de la carpeta publicada, <see cref="AlmacenModeloLlamadas"/>) si
        /// existe; si no, el del deploy. Se mira en cada llamada: al promover, la siguiente puntuación ya carga el nuevo.
        /// </summary>
        public static string RutaPorDefecto => AlmacenModeloLlamadas.Produccion().RutaModeloActivo ?? RutaDelDeploy;

        internal static MLContext CrearContexto() => Entrenador.CrearContexto();

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
                if (!cargados.TryGetValue(ruta, out ModeloCargado cargado) || cargado.Fecha != fecha)
                {
                    MLContext nuevo = CrearContexto();
                    // FileShare.Delete: el job de reentrenamiento puede cambiar el zip (File.Replace) mientras se lee.
                    cargado = new ModeloCargado { Fecha = fecha, Modelo = Entrenador.Cargar(nuevo, ruta), Contexto = nuevo };
                    cargados[ruta] = cargado;
                }
                ml = cargado.Contexto;
                return cargado.Modelo;
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
        /// NestoAPI#618: el modelo por subgrupo que deja el job mensual (C:\NestoAPI\ModelosIA\modelo_llamadas_subgrupo.zip). No hay
        /// copia en el deploy: null si todavía no se ha promovido ninguno.
        /// </summary>
        public static string RutaSubgrupo => AlmacenModeloLlamadas.ProduccionSubgrupo().RutaModeloActivo;

        /// <summary>NestoAPI#618: probabilidad de pedido CON el subgrupo de cada entrada, en el mismo orden.</summary>
        public static List<float> PuntuarSubgrupo(IList<ModeloContactoSubgrupoEntrada> entradas, string ruta)
        {
            if (entradas == null || entradas.Count == 0)
            {
                return new List<float>();
            }
            ITransformer modelo = Cargar(ruta, out MLContext ml);
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
