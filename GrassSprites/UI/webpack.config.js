const path = require("path");
const webpack = require("webpack");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const MOD = require("./mod.json");

const CSII_USERDATAPATH = process.env.CSII_USERDATAPATH;
if (!CSII_USERDATAPATH) {
  throw "CSII_USERDATAPATH environment variable is not set; ensure the CSII Modding Toolchain is installed correctly";
}

const OUTPUT_DIR = `${CSII_USERDATAPATH}\\Mods\\${MOD.id}`;

module.exports = {
  devtool: false,
  mode: "production",
  stats: "minimal",
  entry: {
    [MOD.id]: "./src/index.tsx",
  },
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cs2/input": "cs2/input",
    "cs2/utils": "cs2/utils",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: "ts-loader",
        exclude: /node_modules/,
      },
      {
        test: /\.s?css$/,
        include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              url: true,
              importLoaders: 1,
              modules: {
                auto: true,
                exportLocalsConvention: "camelCase",
                localIdentName: "[local]_[hash:base64:3]",
              },
            },
          },
          "sass-loader",
        ],
      },
      {
        test: /\.(png|jpe?g|gif|svg)$/i,
        type: "asset/resource",
        generator: {
          filename: "images/[name][ext][query]",
        },
      },
    ],
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: ["node_modules", path.join(__dirname, "src")],
    alias: {
      "mod.json": path.resolve(__dirname, "mod.json"),
    },
  },
  output: {
    path: path.resolve(__dirname, OUTPUT_DIR),
    library: {
      type: "module",
    },
    publicPath: "coui://ui-mods/",
  },
  experiments: {
    outputModule: true,
  },
  plugins: [
    new webpack.EvalSourceMapDevToolPlugin({}),
    new MiniCssExtractPlugin(),
  ],
};
