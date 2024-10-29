using System.Collections.Generic;

namespace NestoAPI.Models
{
    public class Constantes
    {
        public class Agencias
        {
            public const int AGENCIA_GLOVO = 7;
            public const int ESTADO_EN_CURSO = 0;
        }

        public static class Almacenes
        {
            public const string ALCOBENDAS = "ALC";
            public const string ALGETE = "ALG";
            public const string REINA = "REI";
        }

        public static class Clientes
        {
            public const char SEPARADOR_TELEFONOS = '/';
            public const short ESTADO_DISTRIBUIDORES = 6;
            public static class Estados
            {
                public const short NULO = -1;
                public const short VISITA_PRESENCIAL = 0;
                public const short VISITA_TELEFONICA = 9;
                public const short PRIMERA_VISITA = 5;
                public const short DISTRIBUIDOR = 6;
                public const short COMISIONA_SIN_VISITA = 7;
                public const short SIN_ACCION_COMERCIAL_SOLO_ESTETICA = 93;
                public const short SIN_ACCION_COMERCIAL_SOLO_PELUQUERIA = 11;
                public const short SIN_ACCION_COMERCIAL_ESTETICA_Y_PELUQUERIA = 22;
            }

            public static class EstadosMandatos
            {
                public const int EN_PODER_DEL_CLIENTE = 2;
            }
            public const short CARGO_POR_DEFECTO = 14;
            public const string DIAS_EN_SERVIR_POR_DEFECTO = "11111";
            public const string GRUPO_POR_DEFECTO = "0";
            public const string SECUENCIA_POR_DEFECTO = "FRST";

            public static class PersonasContacto
            {
                public const short ESTADO_POR_DEFECTO = 0;
                public const short CARGO_FACTURA_POR_CORREO = 22;
                public const short CARGO_FACTURAS_TRIMESTRE_POR_CORREO = 28;
                public const short CARGO_COBROS = 1;
            }

            public static class TiposExtracto
            {
                public const string TIPO_FACTURA = "1";
                public const string TIPO_CARTERA = "2";
            }
        }

        public static class Correos
        {
            public const string COMPRAS = "compras@nuevavision.es";
            public const string CORREO_ADMON = "administracion@nuevavision.es";
            public const string CORREO_DIRECCION = "direccion@nuevavision.es";
            public const string TIENDA_ALCOBENDAS = "alcobendas@nuevavision.es";
            public const string INFORMATICA = "carlosadrian@nuevavision.es";
            public const string LOGISTICA = "logistica@nuevavision.es";
            public const string TIENDA_ONLINE = "tiendaonline@nuevavision.es";
            public const string TIENDA_REINA = "tienda@nuevavision.es";            
        }

        public static class Cuentas
        {
            public const string COMISIONES_BANCO_COBRO_TPV = "62600008";
            public const string CUENTA_PORTES_VENTA_GENERAL = "75900000";
            public const string CUENTA_PORTES_GLOVO = "62400017";
            public const string CUENTA_PORTES_CEX = "62400005";
            public const string CUENTA_PORTES_ONTIME = "62400002";
            public const short ESTADO_ACTIVA = 0;
            public const short NIVEL_MAXIMO = 8;
        }

        public static class DiariosContables
        {
            public const string COMISIONES_BANCO = "_ComisBanc";
        }

        public static class DiariosProducto
        {
            public const string MONTAR_KIT = "_MontarKit";
        }

        public static class Empresas
        {
            public const string DELEGACION_POR_DEFECTO = "ALG";
            public const string EMPRESA_ESPEJO_POR_DEFECTO = "3";
            public const string EMPRESA_POR_DEFECTO = "1";
            public const string FORMA_VENTA_POR_DEFECTO = "VAR";
            public const string IVA_POR_DEFECTO = "G21";
        }

        public static class EstadosLineaVenta
        {
            public const int PRESUPUESTO = -3;
            public const int PENDIENTE = -1;
            public const int EN_CURSO = 1;
            public const int ALBARAN = 2;
            public const int FACTURA = 4;
        }

        public static class ExtractosCliente
        {
            public static class Estados
            {
                public const string DEUDA_VENCIDA = "DVD";
                public const string RETENIDO = "RTN";
            }
            public static class TiposApunte
            {
                public const string PAGO = "3";
                public const string IMPAGADO = "4";
            }
        }

        public static class Facturas
        {
            public static class TiposDocumento
            {
                public const string FACTURA = "FACTURA";
                public const string FACTURA_RECTIFICATIVA = "FACTURA RECTIFICATIVA";
                public const string FACTURA_PROFORMA = "FACTURA PROFORMA";
                public const string PEDIDO = "PEDIDO";
                public const string NOTA_ENTREGA = "NOTA DE ENTREGA";
            }
        }

        public static class FormasPago
        {
            public const string EFECTIVO = "EFC";
            public const string RECIBO_BANCARIO = "RCB";
        }

        public static class FormasVenta
        {
            public const string AMAZON = "STK";
            public const string TIENDA_ONLINE = "WEB";
        }

        public static class NivelRiesgoPagos
        {
            public const short NO_TIENE_DEUDA = 1;
            public const short TIENE_DEUDA_NO_VENCIDA = 2;
            public const short TIENE_DEUDA_VENCIDA = 3;
            public const short TIENE_IMPAGADOS_PENDIENTES = 4;
            public const short CONTADO_RIGUROSO = 5;
        }

        public static class Pedidos
        {
            public const string PERIODO_FACTURACION_FIN_DE_MES = "FDM";
            public const string PERIODO_FACTURACION_NORMAL = "NRM";
            public const string RUTA_GLOVO = "GLV";
        }
        public static class PlazosPago
        {
            public const string CONTADO = "CONTADO";
            public const string CONTADO_RIGUROSO = "CR";
            public const string PREPAGO = "PRE";
        }

        public static class Portes
        {
            public const decimal PROVINCIAL = 3.5M;
            public const decimal PENINSULAR = 7M;
            public const decimal INCREMENTO_REEMBOLSO = 0M;
        }

        public static class Productos
        {
            public const string ALMACEN_POR_DEFECTO = "ALG";
            public const string ALMACEN_TIENDA = "REI";
            public const short ESTADO_A_EXTINGUIR = 4;
            public const short ESTADO_NO_SOBRE_PEDIDO = 0;
            public const string FAMILIA_BONIFICACION = "Bonificac";
            public const string GRUPO_APARATOS = "APA";
            public const string GRUPO_COSMETICA = "COS";
            public const string GRUPO_MATERIAS_PRIMAS = "MTP";
            public const string GRUPO_PELUQUERIA = "PEL";
            public const string SUBGRUPO_MUESTRAS = "MMP";
            public const int DEPOSITO_DIAS_ESTADISTICA = 60;
        }

        public static class Proveedores
        {
            public static class PersonasContacto
            {
                public const int INFORMACION_PRODUCTO_DEPOSITO = 27;
                public const int RECEPCION_PEDIDOS = 3;
            }
        }
        public class Sedes
        {
            public static List<string> ListaSedes = new List<string>
            {
                "ALG",
                "REI",
                "ALC"
            };
        }
        public static class TiposExtractoCliente
        {
            public const string SIN_ESPECIFICAR = "0";
            public const string FACTURA = "1";
            public const string CARTERA = "2";
            public const string PAGO = "3";
            public const string IMPAGADO = "4";
        }

        public static class TiposLineaCompra
        {
            public const string TEXTO = "0";
            public const string PRODUCTO = "1";
            public const string CUENTA_CONTABLE = "2";
            public const string INMOVILIZADO = "3";
        }

        public static class TiposLineaVenta
        {
            public const int TEXTO = 0;
            public const int PRODUCTO = 1;
            public const int CUENTA_CONTABLE = 2;
            public const int INMOVILIZADO = 3;
        }

        public static class Ubicaciones
        {
            //public const int ESTADO_A_INSERTAR = -100;
            public const int ESTADO_A_MODIFICAR_CANTIDAD = -101;
            public const int ESTADO_REGISTRO_MONTAR_KITS = -102;
            public const int UBICADO = 0;
            public const int PENDIENTE_UBICAR = 2;
            public const int RESERVADO_PICKING = 3;
            public const int RESERVADO_REPOSICION = 3;
        }

        public static class Picking
        {
            public const int HORA_MAXIMA_AMPLIAR_PEDIDOS = 11;
        }

        public static class ClientesEspeciales
        {
            public const string EL_EDEN = "15191";
            public const string TIENDA_ONLINE = "31517";
            public const string AMAZON = "32624";
        }

        public static class Vendedores
        {
            public const int ESTADO_VENDEDOR_PRESENCIAL = 0;
            public const int ESTADO_VENDEDOR_MINI = 2;
            public const int ESTADO_VENDEDOR_TELEFONICO = 9;
            public const int ESTADO_VENDEDOR_PELUQUERIA = 4;
            public const int ESTADO_VENDEDOR_PARA_ANULAR = 99;
            public const string VENDEDOR_GENERAL = "NV";
        }

        public static class SeguimientosCliente
        {
            public static class Tipos
            {
                public const string TIPO_VISITA_PRESENCIAL = "V";
                public const string TIPO_VISITA_TELEFONICA = "T";
            }
        }

        public static class Contabilidad
        {
            public static class Diarios {
                public const string DIARIO_CIERRE = "_ASIENTCIE";
            }

            public static class TiposCuenta
            {
                public const string CUENTA_CONTABLE = "1";
                public const string CLIENTE = "2";
                public const string PROVEEDOR = "3";
            }
        }
    }
}