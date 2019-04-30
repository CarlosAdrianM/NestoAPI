namespace NestoAPI.Models
{
    public class Constantes
    {
        public class Agencias
        {
            public const int AGENCIA_GLOVO = 7;
            public const int ESTADO_EN_CURSO = 0;
        }

        public class Almacenes
        {
            public const string ALGETE = "ALG";
            public const string REINA = "REI";
        }

        public class Clientes
        {
            public class Estados
            {
                public const int VISITA_PRESENCIAL = 0;
                public const int PRIMERA_VISITA = 5;
            }
            public class EstadosMandatos
            {
                public const int EN_PODER_DEL_CLIENTE = 2;
            }
            public const short CARGO_POR_DEFECTO = 14;
            public const string DIAS_EN_SERVIR_POR_DEFECT0 = "11111";
            public const string GRUPO_POR_DEFECTO = "0";
            public const string SECUENCIA_POR_DEFECTO = "FRST";
        }

        public class Correos
        {
            public const string CORREO_DIRECCION = "direccion@nuevavision.es";
            public const string TIENDA_REINA = "tienda@nuevavision.es";
            public const string TIENDA_ONLINE = "tiendaonline@nuevavision.es";
        }

        public class Cuentas
        {
            public const string CUENTA_PORTES_GLOVO = "62400017";
        }

        public class Empresas
        {
            public const string DELEGACION_POR_DEFECTO = "ALG";
            public const string EMPRESA_ESPEJO_POR_DEFECTO = "3";
            public const string EMPRESA_POR_DEFECTO = "1";
            public const string FORMA_VENTA_POR_DEFECTO = "VAR";
            public const string IVA_POR_DEFECTO = "G21";
        }

        public class EstadosLineaVenta
        {
            public const int PRESUPUESTO = -3;
            public const int PENDIENTE = -1;
            public const int EN_CURSO = 1;
            public const int ALBARAN = 2;
            public const int FACTURA = 4;
        }

        public class Pedidos
        {
            public const string PERIODO_FACTURACION_FIN_DE_MES = "FDM";
            public const string PERIODO_FACTURACION_NORMAL = "NRM";
            public const string RUTA_GLOVO = "GLV";
        }

        public class Productos
        {
            public const string ALMACEN_POR_DEFECTO = "ALG";
            public const string ALMACEN_TIENDA = "REI";
            public const short ESTADO_NO_SOBRE_PEDIDO = 0;
            public const string GRUPO_COSMETICA = "COS";
            public const string GRUPO_PELUQUERIA = "PEL";
            public const string SUBGRUPO_MUESTRAS = "MMP";
        }

        public class TiposLineaVenta
        {
            public const int TEXTO = 0;
            public const int PRODUCTO = 1;
            public const int CUENTA_CONTABLE = 2;
            public const int INMOVILIZADO = 3;
        }

        public class Ubicaciones
        {
            public const int UBICADO = 0;
            public const int PENDIENTE_UBICAR = 2;
            public const int RESERVADO_PICKING = 3;
            public const int RESERVADO_REPOSICION = 3;
        }

        public class Picking
        {
            public const int HORA_MAXIMA_AMPLIAR_PEDIDOS = 11;
        }

        public class ClientesEspeciales
        {
            public const string EL_EDEN = "15191";
        }

        public class Vendedores
        {
            public const string VENDEDOR_GENERAL = "NV";
        }
    }
}