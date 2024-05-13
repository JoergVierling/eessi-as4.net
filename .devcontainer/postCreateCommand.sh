
dotnet workload update
dotnet tool install -g JetBrains.ReSharper.GlobalTools

cat << \EOF >> ~/.bash_profile
# Add .NET Core SDK tools
export PATH="$PATH:/root/.dotnet/tools"
EOF